using FluentValidation;
using Microsoft.Extensions.Configuration;
using Vaulta.Marketplace.Domain;
using Vaulta.Orders.Application;
using Vaulta.Payments.Application.Commands;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.SharedKernel;
using Vaulta.Wallets.Application;

namespace Vaulta.Payments.Application;

public sealed class PaymentCommandHandlers(
    IPaymentStore store,
    IPaymentGateway gateway,
    IOrderStore orderStore,
    IWalletStore walletStore,
    IConfiguration configuration,
    IClock clock)
{
    public async Task<PaymentDto> Handle(InitiatePaymentCommand command, CancellationToken cancellationToken)
    {
        await new CreatePaymentValidator().ValidateAndThrowAsync(command.Request, cancellationToken);

        var order = await orderStore.FindOrder(command.Request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order not found.");

        if (order.BuyerId != command.UserId)
            throw new ForbiddenException("Only the buyer can initiate payment for this order.");

        if (order.Status != Orders.Domain.OrderRules.PendingStatus)
            throw new ConflictException($"Order is not in pending status. Current status: {order.Status}");

        var existing = await store.FindByOrderId(order.Id, cancellationToken);
        if (existing is not null && existing.Status == PaymentRules.ConfirmedStatus)
            throw new ConflictException("Payment already confirmed for this order.");

        var now = clock.UtcNow;
        var splits = await BuildSplits(order, cancellationToken);

        var gatewayRequest = new GatewayPaymentRequest(
            order.TotalAmountBrl,
            command.Request.BillingType,
            command.Request.CustomerAsaasId,
            order.Id.ToString(),
            $"Vaulta Order {order.Id:N}",
            splits);

        var result = await gateway.CreatePaymentAsync(gatewayRequest, cancellationToken);

        var transaction = PaymentTransaction.Create(
            order.Id,
            order.BuyerId,
            order.SellerId,
            order.TotalAmountBrl,
            command.Request.BillingType,
            command.Request.CustomerAsaasId,
            now);

        foreach (var split in splits)
            transaction.AddSplit(split.WalletId, split.FixedValue, split.PercentualValue, split.ExternalReference, split.Description);

        transaction.SetCheckoutInfo(result.CheckoutUrl, result.PixQrCode, result.BankSlipUrl);
        store.Add(transaction);
        await store.Save(cancellationToken);

        return MapPayment(transaction);
    }

    public async Task Handle(ProcessWebhookCommand command, CancellationToken cancellationToken)
    {
        if (await store.HasProcessedWebhook(command.PaymentId, command.EventType, cancellationToken))
            return;

        var webhookEvent = WebhookEvent.Receive(command.EventType, command.PaymentId, command.Payload, clock.UtcNow);

        try
        {
            var transaction = await store.FindByAsaasId(command.PaymentId, cancellationToken);
            if (transaction is null)
            {
                webhookEvent.MarkFailed("Payment transaction not found in Vaulta.");
                store.AddWebhookEvent(webhookEvent);
                await store.Save(cancellationToken);
                return;
            }

            switch (command.EventType)
            {
                case "PAYMENT_RECEIVED" or "PAYMENT_CONFIRMED":
                    var netValue = ParseNetValue(command.Payload);
                    transaction.Confirm(command.PaymentId, netValue, clock.UtcNow);

                    var order = await orderStore.FindOrder(transaction.OrderId, cancellationToken);
                    order?.MarkAsPaid(command.PaymentId, clock.UtcNow);
                    break;

                case "PAYMENT_OVERDUE" or "PAYMENT_FAILED":
                    transaction.Fail($"Asaas event: {command.EventType}", clock.UtcNow);
                    break;

                case "PAYMENT_REFUNDED":
                    transaction.Refund(clock.UtcNow);
                    break;

                default:
                    webhookEvent.MarkProcessed();
                    store.AddWebhookEvent(webhookEvent);
                    await store.Save(cancellationToken);
                    return;
            }

            webhookEvent.MarkProcessed();
            store.AddWebhookEvent(webhookEvent);
            await store.Save(cancellationToken);
        }
        catch (Exception ex)
        {
            webhookEvent.MarkFailed(ex.Message);
            store.AddWebhookEvent(webhookEvent);
            await store.Save(cancellationToken);
            throw;
        }
    }

    private async Task<List<SplitConfig>> BuildSplits(Orders.Domain.Order order, CancellationToken cancellationToken)
    {
        var platformFee = MarketplaceRules.CalculatePlatformFee(order.ItemPriceBrl);
        var sellerPayout = MarketplaceRules.CalculateSellerPayout(order.ItemPriceBrl);
        var splits = new List<SplitConfig>();

        var platformWalletId = configuration["PlatformWalletId"];
        if (!string.IsNullOrWhiteSpace(platformWalletId))
        {
            splits.Add(new SplitConfig(
                platformWalletId,
                platformFee,
                null,
                $"platform-fee-{order.Id:N}",
                "Platform commission"));
        }

        var sellerWallet = await walletStore.FindByUserId(order.SellerId, cancellationToken);
        if (sellerWallet is not null)
        {
            splits.Add(new SplitConfig(
                sellerWallet.Id.ToString(),
                sellerPayout,
                null,
                $"seller-payout-{order.Id:N}",
                "Seller payout"));
        }

        return splits;
    }

    private static decimal ParseNetValue(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return 0m;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("netValue", out var netValue))
                return netValue.GetDecimal();
            if (doc.RootElement.TryGetProperty("value", out var value))
                return value.GetDecimal();
        }
        catch { }
        return 0m;
    }

    internal static PaymentDto MapPayment(PaymentTransaction t) => new(
        t.Id, t.OrderId, t.BuyerId, t.SellerId, t.Amount, t.NetAmount, t.Currency,
        t.BillingType, t.Status, t.AsaasPaymentId, t.CheckoutUrl, t.PixQrCode,
        t.BankSlipUrl, t.FailureReason,
        t.Splits.Select(s => new PaymentSplitDto(s.WalletId, s.FixedValue, s.PercentualValue, s.ExternalReference, s.Description)).ToArray(),
        t.CreatedAt, t.ConfirmedAt, t.Version);
}

public sealed class CreatePaymentValidator : AbstractValidator<Contracts.CreatePaymentRequest>
{
    public CreatePaymentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.BillingType).Must(x => x is "PIX" or "CREDIT_CARD" or "BOLETO");
    }
}