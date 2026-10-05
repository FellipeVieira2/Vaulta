using System.Net.Http.Json;
using Vaulta.App.Core.Http;
using Vaulta.Catalog.Contracts;

namespace Vaulta.App.Core.Catalog;

public interface ICatalogClient
{
    Task<CatalogSearchPage> SearchAsync(string query, string? game, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<CatalogPrintingDetails> GetPrintingAsync(Guid printingId, CancellationToken cancellationToken = default);
}

/// <summary>Thin HTTP client for the Catalog module's public read endpoints. No mock data, no caching.</summary>
public sealed class CatalogClient(HttpClient httpClient) : ICatalogClient
{
    public async Task<CatalogSearchPage> SearchAsync(string query, string? game, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var url = $"api/v1/catalog/search?q={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(game))
            url += $"&game={Uri.EscapeDataString(game)}";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        var result = await response.ReadApiJsonAsync<CatalogSearchPage>(cancellationToken);
        return result with { Items = result.Items.Select(x=>x with {ArtworkUrl=Artwork(x.ArtworkUrl)}).ToArray() };
    }

    public async Task<CatalogPrintingDetails> GetPrintingAsync(Guid printingId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/catalog/printings/{printingId}", cancellationToken);
        var result = await response.ReadApiJsonAsync<CatalogPrintingDetails>(cancellationToken);
        return result with {ArtworkUrl=Artwork(result.ArtworkUrl)};
    }
    private string? Artwork(string? value) => value is not null && value.StartsWith("/api/v1/catalog/printings/",StringComparison.Ordinal) && httpClient.BaseAddress is not null
        ? new Uri(httpClient.BaseAddress,value).AbsoluteUri : value;
}
