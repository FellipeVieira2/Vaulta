using Vaulta.Catalog.Application;
using Vaulta.Catalog.Domain;
using FuzzySharp;
using System.Text.RegularExpressions;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class CardEvidenceCatalogMatcher(ICardRecognitionCatalog catalog)
{
    public const string Version = "catalog-evidence-v1";
    private const double ReliableField = 0.8;

    public async Task<CardEvidenceMatchResult> MatchAsync(CardEvidence evidence, string gameCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Usable(evidence.Name, 0.65)
            || Usable(evidence.GameCode) && !string.Equals(evidence.GameCode.Value, gameCode, StringComparison.OrdinalIgnoreCase)) return Empty();
        var hasNumber = Usable(evidence.CollectorNumber);
        var cards = await catalog.FindCandidatesAsync(evidence.Name.Value!, hasNumber ? evidence.CollectorNumber.Value : null, gameCode, cancellationToken);
        var ranked = new List<CardRecognitionCandidate>();
        foreach (var match in cards.DistinctBy(x => x.Card.PrintingId))
        {
            var card = match.Card;
            if (card.PrintingId == Guid.Empty || !string.Equals(card.GameCode, gameCode, StringComparison.OrdinalIgnoreCase)) continue;
            if (Usable(evidence.Language) && !string.Equals(evidence.Language.Value!.Split('-')[0], card.Language.Split('-')[0], StringComparison.OrdinalIgnoreCase)) continue;
            if (Usable(evidence.SetCode) && !string.Equals(evidence.SetCode.Value, match.ProviderSetId, StringComparison.OrdinalIgnoreCase)) continue;
            if (Usable(evidence.SetName) && CatalogNormalizer.NormalizeName(evidence.SetName.Value!) != CatalogNormalizer.NormalizeName(card.SetName)) continue;
            var nameScore = Fuzz.Ratio(CatalogNormalizer.NormalizeName(evidence.Name.Value!), CatalogNormalizer.NormalizeName(card.CardName)) / 100.0;
            if (nameScore < 0.65) continue;
            var numberMatch = hasNumber && NumberMatches(evidence.CollectorNumber.Value!, card.CollectorNumber);
            if (hasNumber && !numberMatch) continue;
            var score = hasNumber && Usable(evidence.Name) && nameScore >= 0.85
                ? Math.Min(0.95, (nameScore + evidence.Name.Confidence + evidence.CollectorNumber.Confidence) / 3)
                : Math.Min(0.7, nameScore * evidence.Name.Confidence);
            ranked.Add(new(card.PrintingId.ToString(), card.CardName, card.SetName, card.CollectorNumber, card.Rarity,
                card.ArtworkUrl, null, null, match.VariantCodes, Math.Round(score, 3), numberMatch));
        }
        if (ranked.Count == 0) return Empty();
        // Uncalibrated evidence never breaks a tie between catalog printings.
        var ambiguous = ranked.Count > 1;
        var candidates = ranked.OrderByDescending(x => x.ConfidenceScore).ThenBy(x => x.PrintingId, StringComparer.Ordinal).Take(5)
            .Select(x => ambiguous ? x with { ConfidenceScore = Math.Min(0.7, x.ConfidenceScore) } : x).ToArray();
        var status = ambiguous ? CardEvidenceMatchStatus.Ambiguous
            : candidates[0].ConfidenceScore >= 0.9 && candidates[0].HasCollectorNumberMatch ? CardEvidenceMatchStatus.Matched : CardEvidenceMatchStatus.NeedsReview;
        return new(status, candidates, Version);
    }

    private static bool Usable(CardEvidenceField field, double threshold = ReliableField) =>
        !string.IsNullOrWhiteSpace(field.Value) && double.IsFinite(field.Confidence) && field.Confidence >= threshold && field.Confidence <= 1;

    private static bool NumberMatches(string evidence, string catalogNumber)
    {
        var seen = evidence.Split('/');
        var stored = catalogNumber.Split('/');
        if (NormalizeNumber(seen[0]) != NormalizeNumber(stored[0])) return false;
        return seen.Length < 2 || stored.Length < 2 || NormalizeNumber(seen[1]) == NormalizeNumber(stored[1]);
    }

    private static string NormalizeNumber(string value) => Regex.Replace(
        Regex.Replace(value, @"\s+", "").ToUpperInvariant(), @"^(?<prefix>[A-Z]*)0+(?=\d)", "${prefix}");

    private static CardEvidenceMatchResult Empty() => new(CardEvidenceMatchStatus.NoMatch, [], Version);
}
