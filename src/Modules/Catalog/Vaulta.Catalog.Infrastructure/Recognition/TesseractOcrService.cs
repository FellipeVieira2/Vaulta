using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Vaulta.SharedKernel;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class OcrOptions
{
    public string ExecutablePath { get; set; } = "tesseract";
    public string? DataPath { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class OcrUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class TesseractOcrService(IOptions<OcrOptions> options) : IOcrService
{
    private readonly SemaphoreSlim _capacity = new(2, 2);

    public async Task<OcrResult> ExtractTextAsync(byte[] imageData, CancellationToken cancellationToken)
    {
        await _capacity.WaitAsync(cancellationToken);
        var path = Path.Combine(Path.GetTempPath(), $"vaulta-ocr-{Guid.NewGuid():N}.png");
        try
        {
            await PreprocessAsync(imageData, path, cancellationToken);
            var settings = options.Value;
            var start = new ProcessStartInfo(settings.ExecutablePath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { path, "stdout", "-l", "eng+por", "--psm", "11" })
                start.ArgumentList.Add(argument);
            if (!string.IsNullOrWhiteSpace(settings.DataPath))
            {
                start.ArgumentList.Add("--tessdata-dir");
                start.ArgumentList.Add(settings.DataPath);
            }
            start.ArgumentList.Add("tsv");
            using var process = new Process { StartInfo = start };
            try { process.Start(); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new OcrUnavailableException("O serviço de leitura de imagens está indisponível.", ex);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var tsv = await output;
                await errors;
                if (process.ExitCode != 0)
                    throw new OcrUnavailableException("O serviço de leitura de imagens não conseguiu processar a foto.");
                return ParseTsv(tsv);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new OcrUnavailableException("A leitura da imagem excedeu o tempo limite. Tente novamente.");
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                try { await Task.WhenAll(output, errors); } catch (OperationCanceledException) { }
            }
        }
        finally
        {
            try { File.Delete(path); } finally { _capacity.Release(); }
        }
    }

    private static async Task PreprocessAsync(byte[] bytes, string path, CancellationToken ct)
    {
        try
        {
            var info = Image.Identify(bytes);
            if ((long)info.Width * info.Height > 40_000_000 || info.Width < 100 || info.Height < 100)
                throw new DomainException("Use uma foto nítida da carta com até 40 megapixels.");
            using var image = Image.Load<Rgba32>(bytes);
            image.Mutate(x => x.AutoOrient());
            // Enlarging provider artwork and increasing contrast can erase fine
            // header/footer text. Preserve pixels; only reduce oversized photos.
            if (image.Width > 1920 || image.Height > 1920)
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(1920, 1920), Mode = ResizeMode.Max
                }));
            await image.SaveAsPngAsync(path, ct);
        }
        catch (UnknownImageFormatException) { throw new DomainException("A imagem enviada não é válida."); }
        catch (InvalidImageContentException) { throw new DomainException("A imagem enviada está corrompida."); }
    }

    internal static OcrResult ParseTsv(string tsv)
    {
        var words = new List<TextRegion>();
        var lines = new List<string>();
        var currentLine = string.Empty;
        var lineWords = new List<string>();
        foreach (var row in tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var cells = row.TrimEnd('\r').Split('\t', 12);
            if (cells.Length != 12 || cells[0] != "5" || string.IsNullOrWhiteSpace(cells[11])) continue;
            var line = string.Join(':', cells[1..5]);
            if (line != currentLine && lineWords.Count > 0)
            {
                lines.Add(string.Join(' ', lineWords));
                lineWords.Clear();
            }
            currentLine = line;
            lineWords.Add(cells[11]);
            if (float.TryParse(cells[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence) && confidence >= 30)
                words.Add(new TextRegion(cells[11], confidence));
        }
        if (lineWords.Count > 0) lines.Add(string.Join(' ', lineWords));
        return new OcrResult(string.Join('\n', lines), words);
    }
}
