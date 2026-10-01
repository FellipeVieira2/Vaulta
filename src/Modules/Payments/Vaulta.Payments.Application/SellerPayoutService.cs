using Vaulta.Orders.Application;
using Vaulta.Orders.Domain;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Application;

public sealed class SellerPayoutService(IPayoutStore store, IPaymentStore payments, IOrderStore orders,
    IPixPayoutGateway gateway, IPayoutIdentity identity, IClock clock)
{
    public async Task<PixDestinationDto?> GetDestination(Guid sellerId, CancellationToken ct)
    {
        var destination = await store.FindDestination(sellerId, ct);
        return destination is null ? null : Map(destination);
    }

    public async Task<PixDestinationDto> RegisterDestination(Guid sellerId, RegisterPixDestinationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Key) || request.Key.Length > 200
            || request.KeyType is not ("CPF" or "CNPJ" or "EMAIL" or "PHONE" or "EVP"))
            throw new DomainException("Informe uma chave Pix e um tipo válido.");
        var key = request.Key.Trim();
        var holder = await gateway.Lookup(key, request.KeyType, ct);
        if (!string.Equals(holder.Key, key, StringComparison.OrdinalIgnoreCase) || holder.KeyType != request.KeyType)
            throw new ConflictException("A consulta Pix não corresponde à chave informada.");
        var destination = await store.FindDestination(sellerId, ct);
        if (destination is null)
        {
            destination = SellerPixDestination.Register(sellerId, holder.Key, holder.KeyType, holder.Name, holder.Document, clock.UtcNow);
            store.Add(destination);
        }
        else destination.Replace(holder.Key, holder.KeyType, holder.Name, holder.Document, clock.UtcNow);
        await store.Save(ct);
        return Map(destination);
    }

    public async Task<PixDestinationReviewDto> GetForReview(Guid reviewerId, Guid sellerId, CancellationToken ct)
    {
        RequireReviewer(reviewerId);
        var destination = await store.FindDestination(sellerId, ct) ?? throw new NotFoundException("Chave Pix não cadastrada.");
        return new(sellerId, destination.Key, destination.KeyType, destination.HolderName,
            destination.HolderDocument, destination.IsVerified, destination.Version);
    }

    public async Task<PixDestinationDto> VerifyDestination(Guid reviewerId, Guid sellerId, VerifyPixDestinationRequest request, CancellationToken ct)
    {
        RequireReviewer(reviewerId);
        var destination = await store.FindDestination(sellerId, ct) ?? throw new NotFoundException("Chave Pix não cadastrada.");
        if (!await identity.IsAvailable(sellerId, ct)) throw new ConflictException("Vendedor indisponível.");
        var holder = await gateway.Lookup(destination.Key, destination.KeyType, ct);
        if (holder.Key != destination.Key || holder.KeyType != destination.KeyType
            || holder.Name != destination.HolderName || holder.Document != destination.HolderDocument)
            throw new ConflictException("A titularidade Pix mudou. Cadastre e revise a chave novamente.");
        destination.Verify(reviewerId, request.Version, request.IdentityEvidenceReference, clock.UtcNow);
        await store.Save(ct);
        return Map(destination);
    }

    public async Task RecordMissing(CancellationToken ct)
    {
        foreach (var paymentId in await store.UnrecordedPayments(ct))
        {
            var payment = await payments.FindById(paymentId, ct);
            if (payment is null) continue;
            var order = await orders.FindOrder(payment.OrderId, ct);
            if (order is null) continue;
            store.Add(SellerPayout.Create(order.Id, payment.Id, order.SellerId,
                order.ItemPriceBrl, order.PlatformFeeBrl, clock.UtcNow));
        }
        await store.Save(ct);
    }

    public async Task Process(Guid payoutId, bool transfersEnabled, CancellationToken ct)
    {
        var payout = await store.Find(payoutId, ct) ?? throw new NotFoundException("Repasse não encontrado.");
        var now = clock.UtcNow;
        if (payout.Status is PayoutStatus.Done or PayoutStatus.Failed) return;
        if (payout.SubmittedAt.HasValue)
        {
            // Claim is never reset to READY: a crash between POST and local save
            // leaves an uncertain operation that must be reconciled, not resent.
            try
            {
                var transfer = await gateway.FindTransfer(payout.TransferId, payout.ExternalReference, payout.SubmittedAt.Value, ct);
                if (transfer is not null) Apply(payout, transfer, now);
                else payout.RequireReconciliation(now);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ConflictException or DomainException)
            {
                payout.RequireReconciliation(now);
            }
            await store.Save(ct);
            return;
        }
        var payment = await payments.FindById(payout.PaymentId, ct);
        var order = await orders.FindOrder(payout.OrderId, ct);
        var destination = await store.FindDestination(payout.SellerId, ct);
        var blocked = payment is null || order is null
            || payment.Status != PaymentRules.ConfirmedStatus || payment.Splits.Count != 0
            || payment.PayoutHoldReason != null
            || payment.Amount != payout.ItemPriceBrl || payment.SellerId != payout.SellerId
            || order.SellerId != payout.SellerId || order.ItemPriceBrl != payout.ItemPriceBrl
            || order.PlatformFeeBrl != payout.PlatformFeeBrl || order.TotalAmountBrl != payout.ItemPriceBrl
            || order.Status is OrderRules.CancelledStatus or OrderRules.RefundedStatus
            || order.PaymentId != payment.AsaasPaymentId
            || !await identity.IsAvailable(payout.SellerId, ct);
        payout.Evaluate(order?.Status == OrderRules.DeliveredStatus && order.DeliveredAt.HasValue,
            payment?.ReceivedAt.HasValue == true, payment?.NetAmount ?? 0m, destination, blocked, now);
        await store.Save(ct);
        if (!transfersEnabled || payout.Status != PayoutStatus.Ready) return;
        // Verify against the currently configured provider account/environment,
        // not only a historical webhook. This also blocks stale/reversed payments
        // and sandbox records that do not exist as settled live charges.
        if (!await gateway.IsPaymentSettled(payment!.AsaasPaymentId!, payout.OrderId, payout.ItemPriceBrl, payout.AmountBrl + payout.PlatformFeeBrl, ct))
        {
            payout.Evaluate(true, true, payment.NetAmount, destination, true, clock.UtcNow);
            await store.Save(ct);
            return;
        }
        if (!await store.Claim(payout, now, ct)) return;

        // Claim and immutable destination snapshot are durable before the only POST.
        // Any network/server error might mean that the provider accepted the transfer.
        try
        {
            var result = await gateway.Transfer(new(payout.AmountBrl, payout.PixKey!, payout.PixKeyType!, payout.ExternalReference), ct);
            Apply(payout, result, clock.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ConflictException or DomainException)
        {
            payout.RequireReconciliation(clock.UtcNow);
        }
        await store.Save(ct);
    }

    public async Task ProcessWebhook(string eventId, string eventType, PixTransfer transfer, CancellationToken ct)
    {
        if (await store.HasWebhook(eventId, ct)) return;
        var expectedStatus = eventType switch
        {
            "TRANSFER_CREATED" => transfer.Status,
            "TRANSFER_PENDING" => "PENDING",
            "TRANSFER_IN_BANK_PROCESSING" => "IN_BANK_PROCESSING",
            "TRANSFER_BLOCKED" => "BLOCKED",
            "TRANSFER_DONE" => "DONE",
            "TRANSFER_FAILED" => "FAILED",
            "TRANSFER_CANCELLED" => "CANCELLED",
            _ => throw new DomainException("Evento de transferência desconhecido.")
        };
        if (transfer.Status != expectedStatus) throw new ConflictException("Transfer status does not match webhook event.");
        var payout = await store.FindByReference(transfer.ExternalReference, ct)
            ?? throw new ConflictException("Repasse ainda não encontrado; repetir o webhook.");
        Apply(payout, transfer, clock.UtcNow);
        store.Add(TransferWebhookReceipt.Create(eventId, payout.Id, clock.UtcNow));
        await store.Save(ct);
    }

    private void RequireReviewer(Guid userId)
    {
        if (!identity.CanReview(userId)) throw new ForbiddenException("Revisão restrita a operadores autorizados.");
    }
    private static void Apply(SellerPayout payout, PixTransfer transfer, DateTimeOffset now) =>
        payout.ApplyTransfer(transfer.Id, transfer.Status, transfer.Value, transfer.ExternalReference, now, transfer.NetValue, transfer.TransferFee);
    private static PixDestinationDto Map(SellerPixDestination d) =>
        new($"••••{d.Key[^Math.Min(4, d.Key.Length)..]}", d.KeyType, d.HolderName,
            d.IsVerified ? "VERIFIED" : "PENDING_IDENTITY_REVIEW", d.VerifiedAt, d.Version);
}
