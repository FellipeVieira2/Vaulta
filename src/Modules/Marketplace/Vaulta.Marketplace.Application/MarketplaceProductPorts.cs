using Vaulta.Marketplace.Contracts;

namespace Vaulta.Marketplace.Application;

public interface IMarketplaceProductQueries
{
    Task<MarketplaceProductPageDto> BrowseProducts(BrowseProductsQueryDto query, CancellationToken ct);
    Task<MarketplaceProductDto?> GetProduct(Guid printingId, Guid? variantId, CancellationToken ct);
    Task<ListingPageDto?> GetOffers(Guid printingId, Guid? variantId, int page, int pageSize, string sort, CancellationToken ct);
}
