using Vaulta.Assets.Application;
using Vaulta.Catalog.Application;
using Vaulta.Collection.Application;
using Vaulta.Marketplace.Application;

namespace Vaulta.Marketplace.Infrastructure;

public sealed class MarketplaceCatalogAdapter(ICatalogCollectionReader catalog) : IMarketplaceCatalog
{
    public Task<Catalog.Contracts.CollectionPrintingDetails?> GetPrinting(Guid printingId, CancellationToken cancellationToken) =>
        catalog.GetPrinting(printingId, cancellationToken);

    public Task<Catalog.Contracts.CollectionVariantDetails?> GetVariant(Guid variantId, CancellationToken cancellationToken) =>
        catalog.GetVariant(variantId, cancellationToken);
}

public sealed class MarketplaceCollectionAdapter(ICollectionQueries collection) : IMarketplaceCollection
{
    public Task<Collection.Contracts.CollectibleItemDto?> GetCollectibleItem(Guid userId, Guid itemId, CancellationToken cancellationToken) =>
        collection.GetItem(userId, itemId, cancellationToken);
}

public sealed class MarketplaceAssetsAdapter(IAssetService assets) : IMarketplaceAssets
{
    public Task<Assets.Contracts.CollectionAssetAccess?> GetAccess(Guid userId, Guid assetId, CancellationToken cancellationToken) =>
        assets.GetCollectionAssetAccess(userId, assetId, cancellationToken);

    public Task<Assets.Contracts.CollectionAssetUrl?> GetUrl(Guid assetId, CancellationToken cancellationToken) =>
        assets.CreatePrivateReadUrl(Guid.Empty, assetId, cancellationToken);
}