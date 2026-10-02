namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class NovaScannerOptions
{
    public bool Enabled { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string ModelId { get; set; } = "amazon.nova-lite-v1:0";
    public int TimeoutSeconds { get; set; } = 15;
    public int MaxTokens { get; set; } = 768;
    public int RetryCount { get; set; } = 1;
    public int RetryDelayMilliseconds { get; set; } = 200;
    public int MaxConcurrency { get; set; } = 2;

    public bool IsValid() => !string.IsNullOrWhiteSpace(ModelId) && ModelId.Length <= 256
        && !string.IsNullOrWhiteSpace(Region) && System.Text.RegularExpressions.Regex.IsMatch(Region, @"^[a-z]{2}(?:-gov)?-[a-z]+-\d+$")
        && TimeoutSeconds is >= 1 and <= 60 && MaxTokens is >= 128 and <= 1024
        && RetryCount is >= 0 and <= 2 && RetryDelayMilliseconds is >= 0 and <= 1000 && MaxConcurrency is >= 1 and <= 4;
}
