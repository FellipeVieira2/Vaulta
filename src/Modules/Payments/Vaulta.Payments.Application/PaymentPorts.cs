using Vaulta.Payments.Domain;

namespace Vaulta.Payments.Application;

public interface IPaymentStore
{
    Task<PaymentTransaction?> FindById(Guid paymentId, CancellationToken cancellationToken);
    Task<PaymentTransaction?> FindByOrderId(Guid orderId, CancellationToken cancellationToken);
    Task<PaymentTransaction?> FindByAsaasId(string asaasPaymentId, CancellationToken cancellationToken);
    void Add(PaymentTransaction transaction);
    void AddWebhookEvent(WebhookEvent webhookEvent);
    Task<bool> HasProcessedWebhook(string asaasPaymentId, string eventType, CancellationToken cancellationToken);
    Task Save(CancellationToken cancellationToken);
}

public interface IPaymentGateway
{
    Task<CreatePaymentResult> CreatePaymentAsync(GatewayPaymentRequest request, CancellationToken cancellationToken);
    Task<CreatePaymentResult?> FindPaymentAsync(GatewayPaymentRequest request, CancellationToken cancellationToken);
    Task<GatewayPixPayload> GetPixPayloadAsync(string paymentId, CancellationToken cancellationToken);
}

public sealed record GatewayPixPayload(string Payload, string? ExpirationDate);

public sealed record GatewayPaymentRequest(
    decimal Amount,
    string BillingType,
    string? CustomerAsaasId,
    string ExternalReference,
    string Description,
    IReadOnlyList<SplitConfig> Splits);

public sealed record SplitConfig(string WalletId, decimal? FixedValue, decimal? PercentualValue, string? ExternalReference, string? Description);

public sealed record CreatePaymentResult(
    string AsaasPaymentId,
    string? CheckoutUrl,
    string? PixQrCode,
    string? BankSlipUrl,
    string Status);
