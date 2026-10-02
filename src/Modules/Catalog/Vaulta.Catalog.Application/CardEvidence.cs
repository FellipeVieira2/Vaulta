namespace Vaulta.Catalog.Application;

// Evidence contains visible fields only. Identity always comes from the catalog.
public sealed record CardEvidenceField(string? Value, double Confidence);

public sealed record CardEvidence(
    CardEvidenceField GameCode,
    CardEvidenceField Name,
    CardEvidenceField CollectorNumber,
    CardEvidenceField SetCode,
    CardEvidenceField SetName,
    CardEvidenceField Language,
    CardEvidenceField Variant,
    string PromptVersion,
    string ModelVersion);

public interface ICardEvidenceExtractor
{
    Task<CardEvidence?> ExtractAsync(byte[] imageData, CancellationToken cancellationToken);
}

public enum CardEvidenceMatchStatus { NoMatch, NeedsReview, Ambiguous, Matched }

public sealed record CardEvidenceMatchResult(
    CardEvidenceMatchStatus Status,
    IReadOnlyList<CardRecognitionCandidate> Candidates,
    string MatcherVersion);
