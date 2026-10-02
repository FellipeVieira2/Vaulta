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
    string ModelVersion,
    CardEvidenceField? Hp = null,
    CardEvidenceField? Finish = null,
    CardEvidenceField? Condition = null,
    CardEvidenceField? IsGraded = null,
    CardEvidenceField? GradingCompany = null,
    CardEvidenceField? Grade = null,
    CardEvidenceField? CertificationNumber = null,
    CardEvidenceField? Rarity = null,
    CardEvidenceField? Year = null,
    CardEvidenceField? CardType = null,
    CardEvidenceField? Stage = null);

public interface ICardEvidenceExtractor
{
    Task<CardEvidence?> ExtractAsync(byte[] imageData, CancellationToken cancellationToken);
    async Task<CardEvidenceExtraction> ExtractWithOutcomeAsync(byte[] imageData, CancellationToken cancellationToken)
    {
        var evidence = await ExtractAsync(imageData, cancellationToken);
        return new(evidence, evidence is null ? "unavailable" : null);
    }
}

public sealed record CardEvidenceExtraction(CardEvidence? Evidence, string? ServiceIssue);

public enum CardEvidenceMatchStatus { NoMatch, NeedsReview, Ambiguous, Matched }

public sealed record CardEvidenceMatchResult(
    CardEvidenceMatchStatus Status,
    IReadOnlyList<CardRecognitionCandidate> Candidates,
    string MatcherVersion);
