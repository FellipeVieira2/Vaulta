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
    double ConfidenceScore);

public sealed record CardScanResultDto(IReadOnlyList<CardScanCandidateDto> Candidates);