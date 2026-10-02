using System.Diagnostics;
using Vaulta.Catalog.Application;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class EvidenceCardRecognitionProvider(ICardEvidenceExtractor extractor, CardEvidenceCatalogMatcher matcher,
    ICardRecognitionProvider? fallback, string providerName, string? fallbackName, string gameCode = "pokemon") : ICardRecognitionProvider
{
    public string GameCode => gameCode;

    public async Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] imageData, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evidence = await extractor.ExtractAsync(imageData, cancellationToken);
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
            activity?.SetTag("scanner.match.status", match.Status.ToString());
            var tags = new[] { new KeyValuePair<string, object?>("provider", providerName), new("status", match.Status.ToString()) };
            ScannerTelemetry.Matches.Add(1, tags);
            ScannerTelemetry.MatcherDuration.Record(timer.Elapsed.TotalMilliseconds, tags);
            if (match.Candidates.Count > 0) return match.Candidates;
        }
        if (fallback is null) return [];
        cancellationToken.ThrowIfCancellationRequested();
        ScannerTelemetry.Fallbacks.Add(1, new KeyValuePair<string, object?>("provider", providerName), new("fallback", fallbackName));
        return await fallback.IdentifyAsync(imageData, cancellationToken);
    }
}
