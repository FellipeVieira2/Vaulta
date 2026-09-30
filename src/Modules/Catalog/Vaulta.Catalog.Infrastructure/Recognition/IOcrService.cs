namespace Vaulta.Catalog.Infrastructure.Recognition;

public interface IOcrService
{
    Task<OcrResult> ExtractTextAsync(byte[] imageData, CancellationToken cancellationToken);
}

public sealed record OcrResult(string RawText, IReadOnlyList<TextRegion> Regions);

public sealed record TextRegion(string Text, float Confidence);