using Vaulta.Payments.Domain;

namespace Vaulta.Payments.Application;

public interface IRefundStore
{
    Task<IReadOnlyList<Guid>> Due(DateTimeOffset now, CancellationToken ct);
    Task<bool> Claim(PaymentTransaction payment, DateTimeOffset now, CancellationToken ct);
}

public interface IRefundGateway
{
    Task<ProviderRefund> Get(string paymentId, Guid orderId, decimal grossAmount, CancellationToken ct);
    Task<ProviderRefund> Request(string paymentId, Guid orderId, decimal grossAmount,
        string billingType, string reason, CancellationToken ct);
}

public sealed record ProviderRefund(bool CanRequest, decimal CompletedAmount, decimal PendingAmount,
    string? RequestUrl = null, bool AwaitingSettlement = false);
