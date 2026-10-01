using Vaulta.Orders.Application;
using Vaulta.Orders.Domain;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Application;

public sealed class PaymentRefundService(IPaymentStore payments, IOrderStore orders,
    IRefundStore refunds, IRefundGateway gateway, IClock clock)
{
    public async Task Request(Guid userId, Guid orderId, string reason, CancellationToken ct)
    {
        var order = await orders.FindOrder(orderId, ct) ?? throw new NotFoundException("Pedido não encontrado.");
        RequireParty(userId, order);
        if (order.Status == OrderRules.RefundedStatus) return;
        if (order.Status is not (OrderRules.PaidStatus or OrderRules.RefundPendingStatus) || order.ShippedAt.HasValue)
            throw new ConflictException("Após o envio, o cancelamento deve ser tratado pelo atendimento.");
        var payment = await payments.FindByOrderId(orderId, ct) ?? throw new ConflictException("Pagamento não encontrado.");
        RequireMatch(order, payment);
        // A durable payout hold is written first. A competing shipment can win
        // the order concurrency check, but then no refund POST is allowed.
        payment.RequestRefund(userId, reason, clock.UtcNow);
        await payments.Save(ct);
        order.RequestRefund(payment.RefundReason!, clock.UtcNow);
        await orders.Save(ct);
    }

    public async Task<OrderRefundDto?> Get(Guid userId, Guid orderId, CancellationToken ct)
    {
        var order = await orders.FindOrder(orderId, ct) ?? throw new NotFoundException("Pedido não encontrado.");
        RequireParty(userId, order);
        var payment = await payments.FindByOrderId(orderId, ct);
        return payment?.RefundStatus is null ? null : new(orderId, payment.RefundStatus,
            payment.Amount, payment.RefundRequestedAt, userId == order.BuyerId ? payment.RefundRequestUrl : null);
    }

    public async Task Process(Guid paymentId, bool requestsEnabled, CancellationToken ct)
    {
        var payment = await payments.FindById(paymentId, ct) ?? throw new NotFoundException("Pagamento não encontrado.");
        if (!payment.RefundRequestedAt.HasValue || payment.RefundStatus == "DONE") return;
        var order = await orders.FindOrder(payment.OrderId, ct) ?? throw new ConflictException("Pedido não encontrado.");
        RequireMatch(order, payment);
        if (order.Status == OrderRules.PaidStatus && !payment.RefundSubmittedAt.HasValue)
        {
            // Recover a crash between the payment hold and the order write.
            order.RequestRefund(payment.RefundReason!, clock.UtcNow);
            await orders.Save(ct);
        }
        if (order.Status is not (OrderRules.RefundPendingStatus or OrderRules.RefundedStatus or OrderRules.CancelledStatus))
        {
            payment.TrackRefund("RECONCILIATION_REQUIRED", clock.UtcNow);
            await payments.Save(ct);
            return;
        }
        if (!requestsEnabled && !payment.RefundSubmittedAt.HasValue) return;
        try
        {
            var state = await gateway.Get(payment.AsaasPaymentId!, order.Id, payment.Amount, ct);
            if (await Apply(order, payment, state, ct)) return;
            if (!payment.RefundSubmittedAt.HasValue && state.AwaitingSettlement && state.PendingAmount == 0 && state.CompletedAmount == 0)
            {
                payment.RetryRefundCheck(clock.UtcNow);
                await payments.Save(ct);
                return;
            }
            if (payment.RefundSubmittedAt.HasValue || !state.CanRequest
                || state.PendingAmount != 0 || state.CompletedAmount != 0)
            {
                payment.TrackRefund(state.PendingAmount > 0 || payment.BillingType == "BOLETO"
                    && payment.RefundRequestUrl is not null && payment.RefundStatus == "PROCESSING"
                    ? "PROCESSING" : "RECONCILIATION_REQUIRED", clock.UtcNow);
                await payments.Save(ct);
                return;
            }
            if (!await refunds.Claim(payment, clock.UtcNow, ct)) return;
            // Persist the submission before the single POST. No blind retries:
            // timeout, server failure and crash are reconciled through GET only.
            state = await gateway.Request(payment.AsaasPaymentId!, order.Id, payment.Amount,
                payment.BillingType, payment.RefundReason!, ct);
            if (await Apply(order, payment, state, ct)) return;
            payment.TrackRefund(state.PendingAmount > 0 || state.RequestUrl is not null
                ? "PROCESSING" : "RECONCILIATION_REQUIRED", clock.UtcNow, state.RequestUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DomainException or ConflictException)
        {
            if (payment.RefundSubmittedAt.HasValue) payment.TrackRefund("RECONCILIATION_REQUIRED", clock.UtcNow);
            else payment.RetryRefundCheck(clock.UtcNow);
        }
        await payments.Save(ct);
    }

    private async Task<bool> Apply(Order order, PaymentTransaction payment, ProviderRefund state, CancellationToken ct)
    {
        if (state.CompletedAmount != payment.Amount || state.PendingAmount != 0) return false;
        order.MarkRefunded(clock.UtcNow);
        await orders.Save(ct);
        payment.Refund(clock.UtcNow);
        await payments.Save(ct);
        return true;
    }
    private static void RequireParty(Guid userId, Order order)
    {
        if (userId != order.BuyerId && userId != order.SellerId)
            throw new ForbiddenException("Somente comprador ou vendedor podem cancelar este pedido.");
    }
    private static void RequireMatch(Order order, PaymentTransaction payment)
    {
        if (payment.BuyerId != order.BuyerId || payment.SellerId != order.SellerId
            || payment.Amount != order.TotalAmountBrl || payment.Currency != "BRL"
            || payment.AsaasPaymentId != order.PaymentId || string.IsNullOrWhiteSpace(payment.AsaasPaymentId))
            throw new ConflictException("Pagamento não corresponde ao pedido.");
    }
}
