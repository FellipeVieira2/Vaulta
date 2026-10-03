namespace Vaulta.Catalog.Infrastructure.MarketResearch;

public sealed class ScannerMarketResearchOptions
{
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 35;
    public int MaxOutputTokens { get; set; } = 2048;
    public int MaxToolCalls { get; set; } = 3;
    public int MaxConcurrency { get; set; } = 2;
    public bool IsValid() => TimeoutSeconds is >= 1 and <= 60 && MaxOutputTokens is >= 128 and <= 4096
        && MaxToolCalls is >= 1 and <= 3 && MaxConcurrency is >= 1 and <= 4;
}
