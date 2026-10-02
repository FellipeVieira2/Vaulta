namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class OpenAiScannerOptions
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-6-luna";
    public int TimeoutSeconds { get; set; } = 15;
    public int MaxConcurrency { get; set; } = 4;
    public int RetryCount { get; set; } = 1;
    public int RetryDelayMilliseconds { get; set; } = 200;
    public string ImageDetail { get; set; } = "high";
    public int MaxOutputTokens { get; set; } = 1280;
    public int MaxImageEdge { get; set; } = 2048;
    public int JpegQuality { get; set; } = 90;

    public bool IsValid() => !string.IsNullOrWhiteSpace(Model) && Model.Length <= 128
        && Model.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_')
        && TimeoutSeconds is >= 1 and <= 60 && MaxConcurrency is >= 1 and <= 4
        && RetryCount is >= 0 and <= 2 && RetryDelayMilliseconds is >= 0 and <= 1000
        && ImageDetail is "low" or "auto" or "high" && MaxOutputTokens is >= 128 and <= 2048
        && MaxImageEdge is >= 1024 and <= 3072 && JpegQuality is >= 80 and <= 95
        && (ApiKey is null || ApiKey.Length <= 512 && !ApiKey.Any(char.IsWhiteSpace) && !ApiKey.Any(char.IsControl));
}
