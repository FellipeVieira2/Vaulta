namespace Vaulta.Catalog.Contracts;

public sealed record CatalogVariantDto(Guid Id, string Code, string Name);
public sealed record CatalogSearchResult(Guid PrintingId, string GameCode, Guid SetId, string SetName, string CardName, string CollectorNumber, string Language, string? Rarity, string? ArtworkUrl);
public sealed record CatalogSearchPage(IReadOnlyList<CatalogSearchResult> Items, int Page, int PageSize, int TotalCount);
public sealed record CatalogPrintingDetails(Guid PrintingId, Guid CardId, Guid SetId, string GameCode, string SetName, string CardName, string CollectorNumber, string Language, string? Rarity, string? ArtworkUrl, IReadOnlyList<CatalogVariantDto> Variants);
public sealed record CollectionPrintingDetails(Guid PrintingId, Guid CardId, Guid SetId, string GameCode, string SetName, string CardName, string CollectorNumber, string Language, string? Rarity, string? ArtworkUrl, bool IsActive);
public sealed record CollectionVariantDetails(Guid VariantId, Guid PrintingId, string Code, string Name, bool IsActive);
