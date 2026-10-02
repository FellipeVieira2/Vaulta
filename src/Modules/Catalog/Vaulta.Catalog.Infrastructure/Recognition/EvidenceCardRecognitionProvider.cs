using System.Diagnostics;
using Vaulta.Catalog.Application;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class EvidenceCardRecognitionProvider(ICardEvidenceExtractor extractor, CardEvidenceCatalogMatcher matcher,
    ICardRecognitionProvider? fallback, string providerName, string? fallbackName, string gameCode = "pokemon", ICardEvidenceCatalogEnricher? enricher = null) : IVisualCardRecognitionProvider
{
    public string GameCode => gameCode;

    public async Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] imageData, CancellationToken cancellationToken)
        => (await IdentifyWithEvidenceAsync(imageData, cancellationToken)).Candidates;

    public async Task<CardRecognitionReading> IdentifyWithEvidenceAsync(byte[] imageData, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extraction = await extractor.ExtractWithOutcomeAsync(imageData, cancellationToken);
        var evidence = extraction.Evidence;
        cancellationToken.ThrowIfCancellationRequested();
        if (evidence is not null)
        {
            using var activity = ScannerTelemetry.Activities.StartActivity("scanner.evidence.match");
            activity?.SetTag("scanner.provider", providerName);
            activity?.SetTag("scanner.matcher.version", CardEvidenceCatalogMatcher.Version);
            activity?.SetTag("scanner.prompt.version", evidence.PromptVersion);
            activity?.SetTag("scanner.model.version", evidence.ModelVersion);
            var timer = Stopwatch.StartNew();
            var match = await matcher.MatchAsync(evidence, GameCode, cancellationToken);
            if (match.Candidates.Count == 0 && enricher is not null && await enricher.EnrichAsync(evidence, GameCode, cancellationToken))
                match = await matcher.MatchAsync(evidence, GameCode, cancellationToken);
            activity?.SetTag("scanner.match.status", match.Status.ToString());
            var tags = new[] { new KeyValuePair<string, object?>("provider", providerName), new("status", match.Status.ToString()) };
            ScannerTelemetry.Matches.Add(1, tags);
            ScannerTelemetry.MatcherDuration.Record(timer.Elapsed.TotalMilliseconds, tags);
            if (match.Candidates.Count > 0) return new(match.Candidates, evidence);
        }
        if (fallback is null) return new([], evidence, extraction.ServiceIssue);
        cancellationToken.ThrowIfCancellationRequested();
        ScannerTelemetry.Fallbacks.Add(1, new KeyValuePair<string, object?>("provider", providerName), new("fallback", fallbackName));
        return new(await fallback.IdentifyAsync(imageData, cancellationToken), evidence, extraction.ServiceIssue);
    }
}
