using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Infrastructure;

public sealed class TcgDexOptions
{
    public string Language { get; set; } = "en";
    public string BaseAddress { get; set; } = "https://api.tcgdex.net/v2/";
}

public sealed class CatalogProviderException(string message, bool isTransient) : Exception(message)
{
    public bool IsTransient { get; } = isTransient;
}

internal sealed class TcgDexProvider(HttpClient httpClient, IOptions<TcgDexOptions> options) : ICatalogProvider
{
    private readonly TcgDexOptions _options = options.Value;
    public string Code => "tcgdex";

    public async Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken cancellationToken)
    {
        var sets = await Get<IReadOnlyList<SetDto>>("sets", cancellationToken);
        return sets.Select(x => new ProviderSet(x.Id, x.Name, x.Id, DateOnly.TryParse(x.ReleaseDate, out var date) ? date : null)).ToArray();
    }

    public async Task<IReadOnlyList<ProviderPrinting>> GetPrintings(string setId, CancellationToken cancellationToken)
    {
        var set = await Get<SetDetailDto>($"sets/{Uri.EscapeDataString(setId)}", cancellationToken);
        return (set.Cards ?? []).Select(card => new ProviderPrinting(
            card.Id,
            card.Name,
            card.LocalId,
            _options.Language,
            card.Rarity,
            card.Image,
            card.Variants?.ToArray() ?? [])).ToArray();
    }

    private async Task<T> Get<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            throw new CatalogProviderException($"TCGdex returned HTTP {(int)response.StatusCode}.", transient);
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
                ?? throw new CatalogProviderException("TCGdex returned an empty response.", true);
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new CatalogProviderException($"TCGdex response could not be parsed ({e.GetType().Name}).", false);
        }
    }

    private sealed record SetDto([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("releaseDate")] string? ReleaseDate);
    private sealed record SetDetailDto([property: JsonPropertyName("cards")] IReadOnlyList<CardDto>? Cards);
    private sealed record CardDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("localId")] string LocalId,
        [property: JsonPropertyName("rarity")] string? Rarity,
        [property: JsonPropertyName("image")] string? Image,
        [property: JsonPropertyName("variants")] IReadOnlyList<string>? Variants);
}
