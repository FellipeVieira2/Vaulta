using System.Globalization;
using Vaulta.App.Core.Http;
using Vaulta.Marketplace.Contracts;

namespace Vaulta.App.Core.Marketplace;

public interface IMarketplaceProductClient
{
    Task<MarketplaceProductPageDto> BrowseProductsAsync(BrowseProductsQueryDto query, CancellationToken ct = default);
    Task<MarketplaceProductDto?> GetProductAsync(Guid printingId, Guid? variantId, CancellationToken ct = default);
    Task<ListingPageDto> GetOffersAsync(Guid printingId, Guid? variantId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<MarketplaceProductFilterOptionsDto> GetFilterOptionsAsync(string? game, string? setQuery = null, int page = 1, CancellationToken ct = default);
}
public sealed class MarketplaceProductClient(HttpClient http) : IMarketplaceProductClient
{
    public static string ProductPath(Guid printingId, Guid? variantId) => $"api/v1/marketplace/products/{printingId}/variants/{variantId?.ToString() ?? "none"}";
    public async Task<MarketplaceProductFilterOptionsDto> GetFilterOptionsAsync(string? game, string? setQuery = null, int page = 1, CancellationToken ct = default)
    {
        var url=$"api/v1/marketplace/product-filters?page={page}&pageSize=20";
        if(!string.IsNullOrWhiteSpace(game))url+="&game="+Uri.EscapeDataString(game);
        if(!string.IsNullOrWhiteSpace(setQuery))url+="&setQuery="+Uri.EscapeDataString(setQuery.Trim());
        using var response=await http.GetAsync(url,ct);
        return await response.ReadApiJsonAsync<MarketplaceProductFilterOptionsDto>(ct);
    }
    public async Task<MarketplaceProductPageDto> BrowseProductsAsync(BrowseProductsQueryDto query, CancellationToken ct = default)
    {
        var parameters = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}", "sort=" + Uri.EscapeDataString(query.Sort) };
        void Add(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) parameters.Add(key + "=" + Uri.EscapeDataString(value.Trim())); }
        Add("query",query.Query); Add("game",query.GameCode); Add("setId",query.SetId?.ToString()); Add("language",query.Language);
        Add("variantCode",query.VariantCode); Add("condition",query.Condition);
        Add("minPriceBrl",query.MinPriceBrl?.ToString(CultureInfo.InvariantCulture)); Add("maxPriceBrl",query.MaxPriceBrl?.ToString(CultureInfo.InvariantCulture));
        if(query.PhotosOnly) Add("photosOnly","true");
        using var response = await http.GetAsync("api/v1/marketplace/products?" + string.Join("&",parameters),ct);
        var page = await response.ReadApiJsonAsync<MarketplaceProductPageDto>(ct);
        return page with { Items = page.Items.Select(Normalize).ToArray() };
    }
    public async Task<MarketplaceProductDto?> GetProductAsync(Guid printingId, Guid? variantId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(ProductPath(printingId,variantId),ct);
        if(response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        return Normalize(await response.ReadApiJsonAsync<MarketplaceProductDto>(ct));
    }
    public async Task<ListingPageDto> GetOffersAsync(Guid printingId, Guid? variantId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(ProductPath(printingId,variantId) + $"/offers?page={page}&pageSize={pageSize}&sort=price_asc",ct);
        var result = await response.ReadApiJsonAsync<ListingPageDto>(ct);
        return result with { Items = result.Items.Select(item=>item.Printing is null ? item : item with { Printing = item.Printing with { ArtworkUrl = Artwork(item.Printing.ArtworkUrl) } }).ToArray() };
    }
    private MarketplaceProductDto Normalize(MarketplaceProductDto product) => product with { Printing = product.Printing with { ArtworkUrl = Artwork(product.Printing.ArtworkUrl) } };
    private string? Artwork(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Uri.TryCreate(value,UriKind.Absolute,out var absolute) && absolute.Scheme is "https" or "http") return absolute.AbsoluteUri;
        return value.StartsWith("/api/v1/catalog/printings/",StringComparison.Ordinal) && http.BaseAddress is not null
            ? new Uri(http.BaseAddress,value).AbsoluteUri : null;
    }
}
