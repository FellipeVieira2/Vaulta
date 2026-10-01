using System.Net.Http.Headers;
using Vaulta.App.Core.Http;
using Vaulta.Catalog.Contracts;

namespace Vaulta.App.Core.Catalog;

public interface IScannerClient
{
    Task<CardScanResultDto> ScanCardAsync(byte[] imageData, string? gameCode, CancellationToken cancellationToken = default);
    Task<CardScanResultDto> SearchCardsAsync(string query, string? gameCode, CancellationToken cancellationToken = default);
    Task<ScannerCardDetailsDto> GetCardDetailsAsync(Guid printingId, CancellationToken cancellationToken = default);
}

public sealed class ScannerClient(HttpClient httpClient) : IScannerClient
{
    public async Task<ScannerCardDetailsDto> GetCardDetailsAsync(Guid printingId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/scanner/printings/{printingId}", cancellationToken);
        return await response.ReadApiJsonAsync<ScannerCardDetailsDto>(cancellationToken);
    }

    public const int MaxImageBytes = 15 * 1024 * 1024;

    public async Task<CardScanResultDto> ScanCardAsync(byte[] imageData, string? gameCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        if (imageData.Length == 0 || imageData.Length > MaxImageBytes)
            throw new ArgumentException("Envie uma foto de até 15 MB.", nameof(imageData));

        var (mimeType, extension) = DetectFormat(imageData);
        using var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(imageData);
        image.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        content.Add(image, "image", $"card.{extension}");
        using var response = await httpClient.PostAsync(WithGame("api/v1/scanner/identify", gameCode), content, cancellationToken);
        return await response.ReadApiJsonAsync<CardScanResultDto>(cancellationToken);
    }

    public async Task<CardScanResultDto> SearchCardsAsync(string query, string? gameCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Informe o nome da carta.", nameof(query));
        var path = $"api/v1/scanner/search?query={Uri.EscapeDataString(query.Trim())}";
        using var response = await httpClient.GetAsync(WithGame(path, gameCode), cancellationToken);
        return await response.ReadApiJsonAsync<CardScanResultDto>(cancellationToken);
    }

    private static string WithGame(string path, string? gameCode) => string.IsNullOrWhiteSpace(gameCode)
        ? path
        : $"{path}{(path.Contains('?') ? '&' : '?')}gameCode={Uri.EscapeDataString(gameCode.Trim().ToLowerInvariant())}";

    private static (string MimeType, string Extension) DetectFormat(byte[] image)
    {
        if (image.Length >= 3 && image[0] == 0xff && image[1] == 0xd8 && image[2] == 0xff) return ("image/jpeg", "jpg");
        if (image.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ("image/png", "png");
        if (image.Length >= 12 && image.AsSpan(0, 4).SequenceEqual("RIFF"u8) && image.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return ("image/webp", "webp");
        throw new ArgumentException("Use uma imagem JPEG, PNG ou WebP.", nameof(image));
    }
}
