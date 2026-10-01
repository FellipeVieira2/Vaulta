namespace Vaulta.Payments.Contracts;

public sealed record OrderRefundDto(Guid OrderId, string Status, decimal AmountBrl,
    DateTimeOffset? RequestedAt, string? RequestUrl);
