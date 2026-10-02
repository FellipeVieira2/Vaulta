using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Vaulta.Catalog.Infrastructure.Recognition;

internal static class ScannerTelemetry
{
    public static readonly ActivitySource Activities = new("Vaulta.Scanner");
    public static readonly Meter Metrics = new("Vaulta.Scanner");
    public static readonly Counter<long> Extractions = Metrics.CreateCounter<long>("scanner.evidence.extractions");
    public static readonly Counter<long> Calls = Metrics.CreateCounter<long>("scanner.evidence.calls");
    public static readonly Counter<long> Retries = Metrics.CreateCounter<long>("scanner.evidence.retries");
    public static readonly Counter<long> Tokens = Metrics.CreateCounter<long>("scanner.evidence.tokens");
    public static readonly Histogram<double> Duration = Metrics.CreateHistogram<double>("scanner.evidence.duration", "ms");
    public static readonly Histogram<double> MatcherDuration = Metrics.CreateHistogram<double>("scanner.evidence.match.duration", "ms");
    public static readonly Counter<long> Matches = Metrics.CreateCounter<long>("scanner.evidence.matches");
    public static readonly Counter<long> Fallbacks = Metrics.CreateCounter<long>("scanner.evidence.fallbacks");
}
