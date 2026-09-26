namespace Vaulta.Catalog.Contracts;

public sealed record ProviderSet(string ExternalId, string Name, string? Code, DateOnly? ReleaseDate);
public sealed record ProviderPrinting(string ExternalId, string Name, string CollectorNumber, string Language, string? Rarity, string? ImageUrl, IReadOnlyList<string> Variants);
public sealed record CatalogSearchResult(Guid PrintingId, string GameCode, string SetName, string CardName, string CollectorNumber, string Language, string? Rarity);
public sealed record CatalogPrintingDetails(Guid PrintingId, string GameCode, string SetName, string CardName, string CollectorNumber, string Language, string? Rarity, IReadOnlyList<string> Variants);
public sealed record CollectionPrintingDetails(Guid PrintingId, Guid CardId, Guid SetId, string GameCode, string SetName, string CardName, string CollectorNumber, string Language, string? Rarity, string? ArtworkUrl);
public sealed record CollectionVariantDetails(Guid VariantId, Guid PrintingId, string Code, string Name);
