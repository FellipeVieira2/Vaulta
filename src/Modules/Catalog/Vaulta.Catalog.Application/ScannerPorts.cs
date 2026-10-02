namespace Vaulta.Catalog.Application;

public interface IScannerCardDetailsReader
{
    Task<Vaulta.Catalog.Contracts.ScannerCardDetailsDto?> GetAsync(Guid printingId, CancellationToken cancellationToken);
}

public interface IBrlExchangeRateProvider
{
    Task<BrlExchangeRate?> GetAsync(string currency, CancellationToken cancellationToken);
}

public sealed record BrlExchangeRate(string Currency, decimal Rate, DateTimeOffset UpdatedAt);

public interface ICardRecognitionCatalog
{
    Task<IReadOnlyList<RecognitionCatalogCard>> FindCandidatesAsync(string name, string? collectorNumber, string gameCode, CancellationToken cancellationToken);
}

public sealed record RecognitionCatalogCard(Vaulta.Catalog.Contracts.CatalogSearchResult Card, IReadOnlyList<string> VariantCodes, string? ProviderSetId = null);

public interface ICardRecognitionProvider
{
    string GameCode { get; }
    Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] imageData, CancellationToken cancellationToken);
}

public sealed record CardRecognitionCandidate(
    string PrintingId,
    string Name,
    string SetName,
    string CollectorNumber,
    string? Rarity,
    string? ArtworkUrl,
    decimal? EstimatedMarketValueBrl,
    string? Currency,
    IReadOnlyList<string> VariantCodes,
    double ConfidenceScore,
    bool HasCollectorNumberMatch = false);

public interface IExternalIdResolver
{
    Task<IReadOnlyDictionary<string, Guid>> ResolvePrintingIdsAsync(IReadOnlyList<string> externalIds, CancellationToken cancellationToken);
}
