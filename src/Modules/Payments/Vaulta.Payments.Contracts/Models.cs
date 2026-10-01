namespace Vaulta.Payments.Contracts;

public sealed record CreatePaymentRequest(Guid OrderId, string BillingType, string? CustomerAsaasId);
public sealed record PaymentSplitDto(string WalletId, decimal? FixedValue, decimal? PercentualValue, string? ExternalReference, string? Description);
public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    Guid BuyerId,
    Guid SellerId,
    decimal Amount,
    decimal NetAmount,
    string Currency,
    string BillingType,
    string Status,
    string? AsaasPaymentId,
    string? CheckoutUrl,
    string? PixQrCode,
    string? BankSlipUrl,
    string? FailureReason,
    IReadOnlyList<PaymentSplitDto> Splits,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    Guid Version,
    string? PixExpirationDate = null);
public sealed record WebhookPayload(string Event, string PaymentId, string? Status, decimal? Value, decimal? NetValue);
