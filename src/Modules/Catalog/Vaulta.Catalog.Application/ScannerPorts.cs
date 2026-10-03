namespace Vaulta.Catalog.Application;

public interface IScannerWebMarketResearchProvider
{
    Task<Vaulta.Catalog.Contracts.ScannerMarketResearchResultDto?> ResearchAsync(
        Vaulta.Catalog.Contracts.CardVisualIdentificationDto identification, byte[]? image, CancellationToken cancellationToken);
}

public interface IScannerMarketResearch
{
    Task<Vaulta.Catalog.Contracts.ScannerMarketResearchResultDto?> ResearchAsync(
        Vaulta.Catalog.Contracts.CardVisualIdentificationDto identification, byte[]? image, CancellationToken cancellationToken);
}

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

public sealed record RecognitionCatalogCard(Vaulta.Catalog.Contracts.CatalogSearchResult Card, IReadOnlyList<string> VariantCodes, string? ProviderSetId = null,
    IReadOnlyList<string>? NormalizedSetNameAliases = null);

public interface ICardRecognitionProvider
{
    string GameCode { get; }
    Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] imageData, CancellationToken cancellationToken);
}

public sealed record CardRecognitionReading(IReadOnlyList<CardRecognitionCandidate> Candidates, CardEvidence? Evidence, string? ServiceIssue = null);
public interface IVisualCardRecognitionProvider : ICardRecognitionProvider
{
    Task<CardRecognitionReading> IdentifyWithEvidenceAsync(byte[] imageData, CancellationToken cancellationToken);
}

public interface ICardEvidenceCatalogEnricher
{
    Task<bool> EnrichAsync(CardEvidence evidence, string gameCode, CancellationToken cancellationToken);
}

public interface ICatalogDiscoveryImporter
{
    Task<bool> ImportAsync(IReadOnlyList<ProviderSetDetails> sets, CancellationToken cancellationToken);
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
