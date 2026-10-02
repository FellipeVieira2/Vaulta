namespace Vaulta.Marketplace.Contracts;

public sealed record CreateListingDraftRequest(string ClientDraftKey, Guid CollectibleItemId, Guid PrintingId, Guid? VariantId, string Condition, decimal? PriceBrl, string? Description);
public sealed record UpdateListingDraftRequest(string Condition, decimal? PriceBrl, string? Description, Guid Version);
public sealed record ListingDraftPhotoRequest(Guid AssetId, string Type, int SortOrder, Guid Version);
public sealed record PublishListingDraftRequest(string IdempotencyKey, Guid Version);
public sealed record ListingDraftDto(Guid Id, string ClientDraftKey, Guid SellerUserId, Guid CollectibleItemId, Guid PrintingId, Guid? VariantId, string Condition, decimal? PriceBrl, string Currency, string Status, string? Description, IReadOnlyList<ListingPhotoDto> Photos, DateTimeOffset CreatedAt, Guid Version);
