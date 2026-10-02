namespace Vaulta.Catalog.Contracts;

public static class ScannerConfidencePolicy
{
    public const double AutoAcceptThreshold = .8;
}

public sealed record CardScanRequest(string ImageBase64, string? GameCode);

public sealed record CardScanCandidateDto(
    Guid PrintingId,
    string Name,
    string SetName,
    string CollectorNumber,
    string? Rarity,
    string? ArtworkUrl,
    decimal? EstimatedMarketValueBrl,
    string? Currency,
    IReadOnlyList<string> VariantCodes,
    double ConfidenceScore,
    string? ExternalPrintingId = null,
    bool HasCollectorNumberMatch = false);

// Visual reading is provisional: it never authorizes collection/marketplace writes.
public sealed record CardVisualIdentificationDto(string Name, string? CollectorNumber, string? Language, string? SetName, double Confidence,
    string? GameCode = null, int? Hp = null, string? Finish = null, string? Condition = null,
    CardCertificationDto? Certification = null, CardVisualAttributesDto? Attributes = null, string? SurfaceTreatment = null);
// A photographed label is evidence only; authenticity requires a separate issuer lookup.
public sealed record CardCertificationDto(string? Company, string? Grade, string? Number, bool IsGraded = true)
{
    public bool Verified => false;
}
public sealed record CardVisualAttributesDto(string? Rarity, int? Year, string? CardType, string? Stage);
public sealed record CardScanResultDto(IReadOnlyList<CardScanCandidateDto> Candidates, CardVisualIdentificationDto? VisualIdentification = null,
    string? ServiceIssue = null);

public sealed record ScannerCardDetailsDto(
    CatalogPrintingDetails Printing,
    IReadOnlyDictionary<string, string> Information,
    IReadOnlyList<CardMarketQuoteDto> MarketQuotes,
    string? Notice,
    DateTimeOffset? FetchedAt = null,
    DateTimeOffset? NextRefreshAt = null);

public sealed record CardMarketQuoteDto(
    Guid VariantId, string VariantName, decimal MarketValueBrl,
    decimal OriginalValue, string OriginalCurrency, string Source,
    DateTimeOffset UpdatedAt, decimal ExchangeRate, DateTimeOffset ExchangeRateAt,
    IReadOnlyList<CardMarketComparisonDto> Comparisons,
    decimal? AverageMarketValueBrl = null,
    int? AveragePeriodDays = null);

public sealed record CardMarketComparisonDto(int Days, decimal AverageBrl, decimal DifferenceBrl, decimal DifferencePercent);
