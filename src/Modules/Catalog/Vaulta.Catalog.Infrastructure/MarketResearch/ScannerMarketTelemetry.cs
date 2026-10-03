using System.Diagnostics.Metrics;

namespace Vaulta.Catalog.Infrastructure.MarketResearch;

internal static class ScannerMarketTelemetry
{
    private static readonly Meter Meter = new("Vaulta.Scanner.MarketResearch");
    public static readonly Counter<long> Tokens = Meter.CreateCounter<long>("scanner.market.web.tokens");
    public static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("scanner.market.web.duration", "ms");
}
