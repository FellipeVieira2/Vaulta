namespace Vaulta.Catalog.Contracts;

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
    string? GameCode = null, int? Hp = null, string? Finish = null, string? Condition = null);
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
