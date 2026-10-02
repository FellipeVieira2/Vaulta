using Vaulta.Catalog.Application;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class NovaCardRecognitionProvider(ICardEvidenceExtractor extractor, CardEvidenceCatalogMatcher matcher, ICardRecognitionProvider fallback) : ICardRecognitionProvider
{
    private static readonly ActivitySource Activities = new("Vaulta.Scanner");
    private static readonly Meter Metrics = new("Vaulta.Scanner");
    private static readonly Counter<long> Matches = Metrics.CreateCounter<long>("scanner.evidence.matches");
    private static readonly Counter<long> Fallbacks = Metrics.CreateCounter<long>("scanner.evidence.fallbacks");

    public string GameCode => fallback.GameCode;
    public async Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] imageData, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = Activities.StartActivity("scanner.evidence.match");
        activity?.SetTag("scanner.matcher.version", CardEvidenceCatalogMatcher.Version);
        var evidence = await extractor.ExtractAsync(imageData, cancellationToken);
        if (evidence is not null)
        {
            activity?.SetTag("scanner.prompt.version", evidence.PromptVersion);
            activity?.SetTag("scanner.model.version", evidence.ModelVersion);
            var match = await matcher.MatchAsync(evidence, GameCode, cancellationToken);
            activity?.SetTag("scanner.match.status", match.Status.ToString());
            Matches.Add(1, new KeyValuePair<string, object?>("status", match.Status.ToString()));
            if (match.Candidates.Count > 0) return match.Candidates;
        }
        Fallbacks.Add(1);
        activity?.SetTag("scanner.fallback", "ocr");
        return await fallback.IdentifyAsync(imageData, cancellationToken);
    }
}
