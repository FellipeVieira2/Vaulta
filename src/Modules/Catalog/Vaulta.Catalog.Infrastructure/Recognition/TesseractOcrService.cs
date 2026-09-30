using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class TesseractOcrService(ILogger<TesseractOcrService> logger) : IOcrService
{
    private const int MaxDimension = 1920;
    private const float MinConfidence = 30f;

    public async Task<OcrResult> ExtractTextAsync(byte[] imageData, CancellationToken cancellationToken)
    {
        try
        {
            var preprocessed = PreprocessImage(imageData);
            return await Task.Run(() => RunOcr(preprocessed), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OCR extraction failed, returning empty result");
            return new OcrResult(string.Empty, []);
        }
    }

    private static byte[] PreprocessImage(byte[] imageData)
    {
        using var image = Image.Load<Rgba32>(imageData);

        // Resize to max dimension for faster OCR
        if (image.Width > MaxDimension || image.Height > MaxDimension)
        {
            var ratio = Math.Min((float)MaxDimension / image.Width, (float)MaxDimension / image.Height);
            image.Mutate(x => x.Resize((int)(image.Width * ratio), (int)(image.Height * ratio)));
        }

        // Convert to grayscale and apply adaptive threshold for better text recognition
        image.Mutate(x => x
            .Grayscale()
            .Contrast(1.3f));

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private OcrResult RunOcr(byte[] preprocessedImage)
    {
        var regions = new List<TextRegion>();
        var rawText = string.Empty;

        try
        {
            using var engine = new TesseractEngine("tessdata", "eng+por", EngineMode.Default);
            engine.SetVariable("tessedit_pageseg_mode", "6"); // Assume uniform block of text

            using var pix = Pix.LoadFromMemory(preprocessedImage);
            using var page = engine.Process(pix);

            rawText = page.GetText().Trim();

            // Extract regions with confidence
            using var iterator = page.GetIterator();
            iterator.Begin();

            do
            {
                var text = iterator.GetText(PageIteratorLevel.Word)?.Trim();
                var confidence = iterator.GetConfidence(PageIteratorLevel.Word);

                if (!string.IsNullOrWhiteSpace(text) && confidence >= MinConfidence)
                {
                    regions.Add(new TextRegion(text, confidence));
                }
            } while (iterator.Next(PageIteratorLevel.Word));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tesseract engine failed, falling back to raw text only");
        }

        return new OcrResult(rawText, regions);
    }
}