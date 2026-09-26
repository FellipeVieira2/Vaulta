namespace Vaulta.Catalog.Application;

// Provider-neutral ingestion models; external JSON DTOs belong to Infrastructure.
public sealed record ProviderSet(string ExternalId, string Name, string? Code, DateOnly? ReleaseDate);
public sealed record ProviderSetDetails(ProviderSet Set, IReadOnlyList<ProviderPrinting> Printings);
public sealed record ProviderVariant(string Code, string Name, string RawValue);
public sealed record ProviderPrinting(string ExternalId, string Name, string CollectorNumber, string Language, string? Rarity, string? ImageUrl, IReadOnlyList<ProviderVariant> Variants);
