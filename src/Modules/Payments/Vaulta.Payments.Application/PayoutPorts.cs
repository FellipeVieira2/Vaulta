using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;

namespace Vaulta.Payments.Application;

public interface IPayoutStore
{
    Task<SellerPixDestination?> FindDestination(Guid sellerId, CancellationToken ct);
    void Add(SellerPixDestination destination);
    void Add(SellerPayout payout);
    void Add(TransferWebhookReceipt receipt);
    Task<SellerPayout?> Find(Guid id, CancellationToken ct);
    Task<SellerPayout?> FindByReference(string externalReference, CancellationToken ct);
    Task<bool> HasWebhook(string eventId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> UnrecordedPayments(CancellationToken ct);
    Task<IReadOnlyList<Guid>> Due(DateTimeOffset now, CancellationToken ct);
    Task<bool> Claim(SellerPayout payout, DateTimeOffset now, CancellationToken ct);
    Task<SellerPayoutPageDto> ListMine(Guid sellerId, int page, int pageSize, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public interface IPixPayoutGateway
{
    Task<PixHolder> Lookup(string key, string keyType, CancellationToken ct);
    Task<bool> IsPaymentSettled(string paymentId, Guid orderId, decimal amount, decimal minimumNetAmount, CancellationToken ct);
    Task<PixTransfer> Transfer(PixTransferRequest request, CancellationToken ct);
    Task<PixTransfer?> FindTransfer(string? transferId, string externalReference, DateTimeOffset submittedAt, CancellationToken ct);
}

public interface IPayoutIdentity
{
    Task<bool> IsAvailable(Guid sellerId, CancellationToken ct);
    bool CanReview(Guid userId);
}

public sealed record PixHolder(string Key, string KeyType, string Name, string Document);
public sealed record PixTransferRequest(decimal Amount, string Key, string KeyType, string ExternalReference);
public sealed record PixTransfer(string Id, string Status, decimal Value, string ExternalReference,
    decimal? NetValue = null, decimal? TransferFee = null);
