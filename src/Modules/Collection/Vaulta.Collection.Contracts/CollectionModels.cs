namespace Vaulta.Collection.Contracts;

public sealed record AcquisitionPrice(decimal Amount, string Currency);
public sealed record AddCollectibleItemsRequest(Guid PrintingId, Guid? VariantId, int Quantity, string Condition, AcquisitionPrice? AcquisitionPrice, DateOnly? AcquisitionDate, string? Notes);
public sealed record UpdateCollectibleItemRequest(string Condition, AcquisitionPrice? AcquisitionPrice, DateOnly? AcquisitionDate, string? Notes, Guid Version);
public sealed record AttachCollectibleItemAssetRequest(Guid AssetId, string Type, int SortOrder);
public sealed record CollectibleItemCreatedDto(Guid Id, Guid Version);
public sealed record AddCollectibleItemsResponse(Guid CollectionEntryId, IReadOnlyList<CollectibleItemCreatedDto> CreatedItems, int Quantity);
public sealed record CollectionAssetDto(Guid AssetId, string Type, int SortOrder, bool IsPrimary, string Url, DateTimeOffset UrlExpiresAt);
public sealed record CollectibleItemDto(Guid Id, Guid CollectionEntryId, string Condition, AcquisitionPrice? AcquisitionPrice, DateOnly? AcquisitionDate, string? Notes, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid Version, IReadOnlyList<CollectionAssetDto> Assets);
public sealed record CollectionEntryListItemDto(Guid CollectionEntryId, Guid PrintingId, Guid? VariantId, string GameCode, string CardName, string SetName, string CollectorNumber, string Language, string? Rarity, string? ArtworkUrl, string? VariantCode, int Quantity, IReadOnlyDictionary<string, int> Conditions);
public sealed record CollectionPageDto(IReadOnlyList<CollectionEntryListItemDto> Items, int Page, int PageSize, int TotalCount);
public sealed record CollectionEntryDetailsDto(Guid CollectionEntryId, Guid PrintingId, Guid? VariantId, string GameCode, string CardName, string SetName, string CollectorNumber, string Language, string? Rarity, string? ArtworkUrl, string? VariantCode, int Quantity, IReadOnlyDictionary<string, int> Conditions, IReadOnlyList<CollectibleItemDto> Items, int ItemsPage, int ItemsPageSize, int ItemsTotalCount);
public sealed record CollectionSummaryDto(int TotalEntries, int TotalItems, IReadOnlyDictionary<string, int> ItemsByGame, IReadOnlyDictionary<string, int> ItemsByCondition);
public sealed record CollectionQuery(string? Query, string? Game, Guid? SetId, string? Condition, Guid? VariantId, int Page, int PageSize, string Sort);
