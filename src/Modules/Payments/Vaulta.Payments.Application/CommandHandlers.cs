using FluentValidation;
using Vaulta.Orders.Application;
using Vaulta.Payments.Application.Commands;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Application;

public sealed class PaymentCommandHandlers(
    IPaymentStore store,
    IPaymentGateway gateway,
    IOrderStore orderStore,
    IClock clock,
    IBuyerCustomerAccess customers)
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

        var customerId = await customers.Resolve(command.UserId, cancellationToken);
        if (command.Request.CustomerAsaasId is not null && command.Request.CustomerAsaasId != customerId)
            throw new ForbiddenException("O cliente da cobrança deve pertencer ao comprador autenticado.");

        var existing = await store.FindByOrderId(order.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status is not (PaymentRules.PendingStatus or PaymentRules.OverdueStatus)
                || existing.BillingType != command.Request.BillingType)
                throw new ConflictException("A payment already exists for this order. Its state must be resolved before another charge is created.");
            existing.BindBuyerCustomer(customerId, clock.UtcNow);
            if (string.IsNullOrWhiteSpace(existing.AsaasPaymentId))
            {
                var recovered = await gateway.FindPaymentAsync(new(order.TotalAmountBrl, existing.BillingType,
                    customerId, order.Id.ToString(), $"Vaulta Order {order.Id:N}", []), cancellationToken)
                    ?? throw new ConflictException("Cobrança sem resposta conclusiva. A consulta não autoriza criar outra cobrança.");
                existing.SetCheckoutInfo(recovered.AsaasPaymentId, recovered.CheckoutUrl, recovered.PixQrCode, recovered.BankSlipUrl);
                await store.Save(cancellationToken);
            }
            await EnsurePixPayload(existing, cancellationToken);
            return MapPayment(existing);
        }

        var now = clock.UtcNow;
        // Seller payout is a separate Pix transfer after buyer-confirmed receipt.
        // A split here would distribute funds before that confirmation.
        IReadOnlyList<SplitConfig> splits = [];

        var gatewayRequest = new GatewayPaymentRequest(
            order.TotalAmountBrl,
            command.Request.BillingType,
            customerId,
            order.Id.ToString(),
            $"Vaulta Order {order.Id:N}",
            splits);

        var transaction = PaymentTransaction.Create(
            order.Id,
            order.BuyerId,
            order.SellerId,
            order.TotalAmountBrl,
            command.Request.BillingType,
            customerId,
            now);

        foreach (var split in splits)
            transaction.AddSplit(split.WalletId, split.FixedValue, split.PercentualValue, split.ExternalReference, split.Description);

        // Reserve the unique order/payment row before calling the gateway. A
        // concurrent request must fail locally, before creating another charge.
        store.Add(transaction);
        await store.Save(cancellationToken);
        var result = await gateway.CreatePaymentAsync(gatewayRequest, cancellationToken);
        transaction.SetCheckoutInfo(result.AsaasPaymentId, result.CheckoutUrl, result.PixQrCode, result.BankSlipUrl);
        await store.Save(cancellationToken);
        await EnsurePixPayload(transaction, cancellationToken);

        return MapPayment(transaction);
    }

    private async Task EnsurePixPayload(PaymentTransaction transaction, CancellationToken ct)
    {
        if (transaction.BillingType != "PIX") return;
        // Persist the charge ID before this read. QR retrieval failure must not
        // turn a retry into a second charge or lose the provider's first ID.
        try
        {
            var payload = await gateway.GetPixPayloadAsync(transaction.AsaasPaymentId!, ct);
            transaction.UpdatePixPayload(payload.Payload, payload.ExpirationDate, clock.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DomainException)
        {
            // A cached QR can expire or change. Keep the durable charge and its
            // hosted invoice, but do not offer an unverified old code on refresh.
            transaction.UpdatePixPayload(null, null, clock.UtcNow);
        }
        await store.Save(ct);
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
                // Do not acknowledge an event whose payment has not been saved
                // yet: Asaas must retry instead of silently losing confirmation.
                throw new ConflictException("The referenced payment is not available yet. Retry this webhook.");
            }

            switch (command.EventType)
            {
                case "PAYMENT_RECEIVED" or "PAYMENT_CONFIRMED":
                    if (transaction.Status == PaymentRules.RefundedStatus) break;
                    var netValue = ParseNetValue(command.Payload);
                    if (netValue < 0 || netValue > transaction.Amount)
                        throw new DomainException("Invalid payment net value.");

                    // Orders live in another DbContext, so there is no shared transaction. The order is saved
                    // first and skipped when already paid, which makes webhook retries safe. Nothing in the
                    // Payments context is mutated until the order write succeeds.
                    var order = await orderStore.FindOrder(transaction.OrderId, cancellationToken);
                    if (order is not null && order.Status == Orders.Domain.OrderRules.PendingStatus)
                    {
                        order.MarkAsPaid(command.PaymentId, clock.UtcNow);
                        await orderStore.Save(cancellationToken);
                    }

                    if (transaction.Status is PaymentRules.PendingStatus or PaymentRules.OverdueStatus)
                        transaction.Confirm(command.PaymentId, netValue, clock.UtcNow);
                    if (command.EventType == "PAYMENT_RECEIVED")
                        transaction.RecordSettlement(netValue, clock.UtcNow);
                    if (order?.Status == Orders.Domain.OrderRules.CancelledStatus)
                    {
                        order.RecordPaymentAfterCancellation(command.PaymentId, clock.UtcNow);
                        await orderStore.Save(cancellationToken);
                        transaction.RefundLateCancelledPayment("Pagamento confirmado após cancelamento antes do envio", clock.UtcNow);
                    }
                    else if (order is null || order.Status == Orders.Domain.OrderRules.RefundedStatus)
                        transaction.HoldPayout("ORDER_UNAVAILABLE", clock.UtcNow);
                    break;

                case "PAYMENT_OVERDUE":
                    transaction.MarkOverdue(clock.UtcNow);
                    break;
                case "PAYMENT_FAILED" or "PAYMENT_CREDIT_CARD_CAPTURE_REFUSED":
                    if (transaction.Status == PaymentRules.ConfirmedStatus)
                    {
                        transaction.HoldPayout(command.EventType, clock.UtcNow);
                        break;
                    }
                    transaction.Fail($"Asaas event: {command.EventType}", clock.UtcNow);
                    break;

                case "PAYMENT_REFUNDED":
                    ValidateFullRefund(command.Payload, transaction);
                    var refundedOrder = await orderStore.FindOrder(transaction.OrderId, cancellationToken)
                        ?? throw new ConflictException("Refunded order not found; retry webhook.");
                    refundedOrder.MarkRefunded(clock.UtcNow);
                    await orderStore.Save(cancellationToken);
                    transaction.Refund(clock.UtcNow);
                    break;

                case "PAYMENT_CHARGEBACK_REQUESTED" or "PAYMENT_CHARGEBACK_DISPUTE" or "PAYMENT_AWAITING_CHARGEBACK_REVERSAL"
                    or "PAYMENT_REFUND_IN_PROGRESS" or "PAYMENT_REFUND_DENIED" or "PAYMENT_PARTIALLY_REFUNDED" or "PAYMENT_RECEIVED_IN_CASH_UNDONE" or "PAYMENT_DELETED":
                    // No automatic release after a dispute/reversal. Provider
                    // reconciliation must establish a new safe financial state.
                    transaction.HoldPayout(command.EventType, clock.UtcNow);
                    if (transaction.RefundRequestedAt.HasValue)
                        transaction.TrackRefund(command.EventType == "PAYMENT_REFUND_IN_PROGRESS" ? "PROCESSING" : "RECONCILIATION_REQUIRED", clock.UtcNow);
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

    private static void ValidateFullRefund(string? payload, PaymentTransaction transaction)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(payload ?? "{}");
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object || !root.TryGetProperty("payment", out var payment)
                || payment.ValueKind != System.Text.Json.JsonValueKind.Object
                || !payment.TryGetProperty("value", out var gross) || gross.ValueKind != System.Text.Json.JsonValueKind.Number
                || !gross.TryGetDecimal(out var amount) || amount != transaction.Amount
                || !payment.TryGetProperty("status", out var status) || status.ValueKind != System.Text.Json.JsonValueKind.String
                || status.GetString() != "REFUNDED"
                || !payment.TryGetProperty("refunds", out var refunds) || refunds.ValueKind != System.Text.Json.JsonValueKind.Array)
                throw new DomainException("Full refund confirmation is required.");
            var total = 0m;
            foreach (var refund in refunds.EnumerateArray())
            {
                if (refund.ValueKind != System.Text.Json.JsonValueKind.Object
                    || !refund.TryGetProperty("status", out var refundStatus) || refundStatus.ValueKind != System.Text.Json.JsonValueKind.String)
                    throw new DomainException("Invalid refund confirmation.");
                if (refundStatus.GetString() != "DONE") continue;
                if (!refund.TryGetProperty("value", out var value) || value.ValueKind != System.Text.Json.JsonValueKind.Number
                    || !value.TryGetDecimal(out var refunded) || refunded <= 0 || refunded != Math.Round(refunded, 2))
                    throw new DomainException("Invalid refunded value.");
                total += refunded;
            }
            if (total != transaction.Amount) throw new ConflictException("Refund amount does not match the full order value.");
        }
        catch (System.Text.Json.JsonException) { throw new DomainException("Invalid refund webhook."); }
    }

    private static decimal ParseNetValue(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) throw new DomainException("Payment net value is required.");
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new DomainException("Payment webhook must be an object.");
            var root = doc.RootElement.TryGetProperty("payment", out var payment) ? payment : doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object
                && root.TryGetProperty("netValue", out var netValue) && netValue.ValueKind == System.Text.Json.JsonValueKind.Number
                && netValue.TryGetDecimal(out var amount))
                return amount;
        }
        catch (System.Text.Json.JsonException) { }
        throw new DomainException("Payment net value is required and must be numeric.");
    }

    public static PaymentDto MapPaymentForBuyer(PaymentTransaction t) => MapPayment(t);
    internal static PaymentDto MapPayment(PaymentTransaction t) => new(
        t.Id, t.OrderId, t.BuyerId, t.SellerId, t.Amount, t.NetAmount, t.Currency,
        t.BillingType, t.Status, t.AsaasPaymentId, t.CheckoutUrl, t.PixQrCode,
        t.BankSlipUrl, t.FailureReason,
        t.Splits.Select(s => new PaymentSplitDto(s.WalletId, s.FixedValue, s.PercentualValue, s.ExternalReference, s.Description)).ToArray(),
        t.CreatedAt, t.ConfirmedAt, t.Version, t.PixExpirationDate);
}

public sealed class CreatePaymentValidator : AbstractValidator<Contracts.CreatePaymentRequest>
{
    public CreatePaymentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.BillingType).Must(x => x is "PIX" or "CREDIT_CARD" or "BOLETO");
    }
}
