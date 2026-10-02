namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class ScannerRecognitionOptions
{
    // Null preserves legacy Nova:Enabled configurations; explicit selection wins.
    public string? Provider { get; set; }
    public string? FallbackProvider { get; set; } = "ocr";
    public bool IsValid() => (Provider is null or "openai" or "nova" or "ocr")
        && (FallbackProvider is null or "openai" or "nova" or "ocr");
}
