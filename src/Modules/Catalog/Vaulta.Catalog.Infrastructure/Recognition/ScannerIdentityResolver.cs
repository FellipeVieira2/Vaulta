using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class ScannerIdentityResolver(CardEvidenceCatalogMatcher matcher, ICardEvidenceCatalogEnricher enricher) : IScannerIdentityResolver
{
    public async Task<IReadOnlyList<CardRecognitionCandidate>> ResolveAsync(CardVisualIdentificationDto card, CancellationToken ct)
    {
        if (card.Confidence < ScannerConfidencePolicy.AutoAcceptThreshold || string.IsNullOrWhiteSpace(card.GameCode)) return [];
        CardEvidenceField Field(string? value) => new(value, value is null ? 0 : card.Confidence);
        var evidence = new CardEvidence(Field(card.GameCode), Field(card.Name), Field(card.CollectorNumber), Field(null), Field(card.SetName),
            Field(card.Language), Field(card.Finish), "market-research", "market-research", Field(card.Hp?.ToString()), Field(card.SurfaceTreatment));
        var match = await matcher.MatchAsync(evidence, card.GameCode, ct);
        if (match.Candidates.Count == 0 && await enricher.EnrichAsync(evidence, card.GameCode, ct))
            match = await matcher.MatchAsync(evidence, card.GameCode, ct);
        return match.Candidates;
    }
}
