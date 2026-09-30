namespace Vaulta.Catalog.Application;

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
    double ConfidenceScore);