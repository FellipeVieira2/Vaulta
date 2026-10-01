namespace Vaulta.Payments.Contracts;

public sealed record RegisterPixDestinationRequest(string Key, string KeyType);
public sealed record VerifyPixDestinationRequest(Guid Version, string IdentityEvidenceReference);
public sealed record PixDestinationDto(string MaskedKey, string KeyType, string HolderName, string Status,
    DateTimeOffset? VerifiedAt, Guid Version);
public sealed record PixDestinationReviewDto(Guid SellerId, string Key, string KeyType, string HolderName,
    string HolderDocument, bool IsVerified, Guid Version);
public sealed record SellerPayoutDto(Guid Id, Guid OrderId, decimal ItemPriceBrl, decimal PlatformFeeBrl,
    decimal AmountBrl, string Currency, string Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    string? MaskedDestinationKey = null, decimal? PaymentFeeBrl = null,
    decimal? TransferFeeBrl = null, decimal? SellerNetBrl = null);
public sealed record SellerPayoutPageDto(IReadOnlyList<SellerPayoutDto> Items, int Page, int PageSize, int TotalCount);
