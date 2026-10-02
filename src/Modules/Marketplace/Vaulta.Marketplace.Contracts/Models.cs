namespace Vaulta.Marketplace.Contracts;

public sealed record SellerProfileRequest(string? Bio, string? Street, string? City, string? State, string? ZipCode);
public sealed record CreateListingRequest(Guid CollectibleItemId, Guid PrintingId, Guid? VariantId, string Condition, decimal PriceBrl, string? Description);
public sealed record UpdateListingRequest(string Condition, decimal PriceBrl, string? Description, Guid Version);
public sealed record ListingPhotoRequest(Guid AssetId, string Type, int SortOrder);

public sealed record SellerProfileDto(Guid UserId, string Status, string? Bio, string? Street, string? City, string? State, string? ZipCode, decimal AverageRating, int TotalSales, DateTimeOffset CreatedAt, Guid Version, int TotalReviews = 0);
public sealed record ListingPhotoDto(Guid AssetId, string Type, int SortOrder, bool IsPrimary, string Url, DateTimeOffset UrlExpiresAt);
public sealed record ListingPrintingDto(string CardName, string SetName, string CollectorNumber, string Language, string GameCode, string? ArtworkUrl, string? VariantCode);
public sealed record BrowseListingsQueryDto(Guid? SellerUserId, Guid? PrintingId, Guid? VariantId, string? Query, string? GameCode, int Page, int PageSize, string Sort);
public sealed record ListingDto(Guid Id, Guid SellerUserId, Guid CollectibleItemId, Guid PrintingId, Guid? VariantId, string Condition, decimal PriceBrl, string Currency, string Status, string? Description, IReadOnlyList<ListingPhotoDto> Photos, DateTimeOffset CreatedAt, Guid Version, decimal SellerAverageRating = 0, int SellerTotalReviews = 0, ListingPrintingDto? Printing = null);
public sealed record ListingPageDto(IReadOnlyList<ListingDto> Items, int Page, int PageSize, int TotalCount);
