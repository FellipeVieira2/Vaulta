namespace Vaulta.Catalog.Domain;

public sealed class Game
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public ICollection<Series> Series { get; set; } = new List<Series>();
    public ICollection<Set> Sets { get; set; } = new List<Set>();
    public ICollection<Card> Cards { get; set; } = new List<Card>();
}

public sealed class Series
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public Game Game { get; set; } = null!;
    public ICollection<Set> Sets { get; set; } = new List<Set>();
}

public sealed class Set
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid? SeriesId { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public string? ReleaseDate { get; set; }
    public string? SymbolAssetKey { get; set; }
    public string? LogoAssetKey { get; set; }
    public Game Game { get; set; } = null!;
    public Series? Series { get; set; }
    public ICollection<Printing> Printings { get; set; } = new List<Printing>();
}

public sealed class Card
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public string? Supertype { get; set; }
    public string? Subtypes { get; set; }
    public string? RulesText { get; set; }
    public string? ImageAssetKey { get; set; }
    public Game Game { get; set; } = null!;
    public ICollection<Printing> Printings { get; set; } = new List<Printing>();
}

public sealed class Printing
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public Guid SetId { get; set; }
    public string CollectorNumber { get; set; } = null!;
    public string NormalizedCollectorNumber { get; set; } = null!;
    public string Language { get; set; } = null!;
    public string? Rarity { get; set; }
    public string? RawRarity { get; set; }
    public string? ExternalArtworkUrl { get; set; }
    public Guid? ArtworkAssetId { get; set; }
    public Guid? ThumbnailAssetId { get; set; }
    public string? ArtworkSha256 { get; set; }
    public string? ArtworkETag { get; set; }
    public DateTimeOffset? ArtworkLastModified { get; set; }
    public string? ArtworkImportStatus { get; set; }
    public string? ArtworkImportError { get; set; }
    public DateTimeOffset? ArtworkCheckedAt { get; set; }
    public string? ArtworkProvider { get; set; }
    public string? MetadataJson { get; set; }
    public string? SourcePricingJson { get; set; }
    public bool IsActive { get; set; } = true;
    public Card Card { get; set; } = null!;
    public Set Set { get; set; } = null!;
    public ICollection<Variant> Variants { get; set; } = new List<Variant>();
}

public sealed class Variant
{
    public Guid Id { get; set; }
    public Guid PrintingId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? RawValue { get; set; }
    public bool IsActive { get; set; } = true;
    public Printing Printing { get; set; } = null!;
}

public sealed class CatalogExternalId
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public Guid EntityId { get; set; }
    public string ExternalId { get; set; } = null!;
    public string? ContentHash { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class CatalogSyncRun
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = null!;
    public string Scope { get; set; } = null!;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Status { get; set; } = null!;
    public int RecordsRead { get; set; }
    public int RecordsCreated { get; set; }
    public int RecordsUpdated { get; set; }
    public int RecordsUnresolved { get; set; }
    public string? ErrorCategory { get; set; }
    public string? ProgressJson { get; set; }
}
