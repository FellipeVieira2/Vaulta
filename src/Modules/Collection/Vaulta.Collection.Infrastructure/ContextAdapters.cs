using Vaulta.Assets.Application;
using Vaulta.Assets.Contracts;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Collection.Application;

namespace Vaulta.Collection.Infrastructure;

internal sealed class CollectionCatalogAdapter(ICatalogCollectionReader reader) : ICollectionCatalog
{
    public Task<CollectionPrintingDetails?> GetPrinting(Guid printingId, CancellationToken cancellationToken) => reader.GetPrinting(printingId, cancellationToken);
    public Task<CollectionVariantDetails?> GetVariant(Guid variantId, CancellationToken cancellationToken) => reader.GetVariant(variantId, cancellationToken);
}

internal sealed class CollectionAssetsAdapter(IAssetService service) : ICollectionAssets
{
    public Task<CollectionAssetAccess?> GetAccess(Guid ownerId, Guid assetId, CancellationToken cancellationToken) => service.GetCollectionAssetAccess(ownerId, assetId, cancellationToken);
    public Task<CollectionAssetUrl?> GetPrivateUrl(Guid ownerId, Guid assetId, CancellationToken cancellationToken) => service.CreatePrivateReadUrl(ownerId, assetId, cancellationToken);
}
