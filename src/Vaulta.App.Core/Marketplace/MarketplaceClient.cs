using System.Net.Http.Json;
using Vaulta.App.Core.Http;
using Vaulta.Assets.Contracts;
using Vaulta.Marketplace.Contracts;

namespace Vaulta.App.Core.Marketplace;

public interface IMarketplaceClient
{
    Task<ListingPageDto> ListActiveListingsAsync(Guid? sellerUserId = null, Guid? printingId = null, int page = 1, int pageSize = 20, string sort = "newest", CancellationToken cancellationToken = default);
    Task<ListingDto?> GetListingAsync(Guid listingId, CancellationToken cancellationToken = default);
    Task<SellerProfileDto?> GetMySellerProfileAsync(CancellationToken cancellationToken = default);
    Task<SellerProfileDto> EnableSellerAsync(SellerProfileRequest request, CancellationToken cancellationToken = default);
    Task<ListingDto> CreateListingAsync(CreateListingRequest request, CancellationToken cancellationToken = default);
    Task UpdateListingAsync(Guid listingId, UpdateListingRequest request, CancellationToken cancellationToken = default);
    Task CancelListingAsync(Guid listingId, Guid version, CancellationToken cancellationToken = default);
    Task AddListingPhotoAsync(Guid listingId, ListingPhotoRequest request, CancellationToken cancellationToken = default);
}

public sealed class MarketplaceClient(HttpClient httpClient) : IMarketplaceClient
{
    public async Task<ListingPageDto> ListActiveListingsAsync(Guid? sellerUserId = null, Guid? printingId = null, int page = 1, int pageSize = 20, string sort = "newest", CancellationToken cancellationToken = default)
    {
        var url = $"api/v1/marketplace/listings?page={page}&pageSize={pageSize}&sort={Uri.EscapeDataString(sort)}";
        if (sellerUserId.HasValue) url += $"&sellerUserId={sellerUserId.Value}";
        if (printingId.HasValue) url += $"&printingId={printingId.Value}";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        return await response.ReadApiJsonAsync<ListingPageDto>(cancellationToken);
    }

    public async Task<ListingDto?> GetListingAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/marketplace/listings/{listingId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        return await response.ReadApiJsonAsync<ListingDto>(cancellationToken);
    }

    public async Task<SellerProfileDto?> GetMySellerProfileAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/v1/me/seller", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        return await response.ReadApiJsonAsync<SellerProfileDto>(cancellationToken);
    }

    public async Task<SellerProfileDto> EnableSellerAsync(SellerProfileRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/v1/me/seller", request, cancellationToken);
        return await response.ReadApiJsonAsync<SellerProfileDto>(cancellationToken);
    }

    public async Task<ListingDto> CreateListingAsync(CreateListingRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/v1/me/seller/listings", request, cancellationToken);
        return await response.ReadApiJsonAsync<ListingDto>(cancellationToken);
    }

    public async Task UpdateListingAsync(Guid listingId, UpdateListingRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"api/v1/me/seller/listings/{listingId}", request, cancellationToken);
        await response.EnsureApiSuccessAsync(cancellationToken);
    }

    public async Task CancelListingAsync(Guid listingId, Guid version, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"api/v1/me/seller/listings/{listingId}?version={version}", cancellationToken);
        await response.EnsureApiSuccessAsync(cancellationToken);
    }

    public async Task AddListingPhotoAsync(Guid listingId, ListingPhotoRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/v1/me/seller/listings/{listingId}/photos", request, cancellationToken);
        await response.EnsureApiSuccessAsync(cancellationToken);
    }
}