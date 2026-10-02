using Vaulta.Assets.Application;
using Vaulta.Catalog.Application;
using Vaulta.Collection.Application;
using Vaulta.Marketplace.Application;

namespace Vaulta.Marketplace.Infrastructure;

public sealed class MarketplaceCatalogAdapter(ICatalogCollectionReader catalog) : IMarketplaceCatalog
{
    public Task<IReadOnlyList<Catalog.Contracts.CollectionPrintingDetails>> GetPrintings(IReadOnlyCollection<Guid> printingIds, CancellationToken cancellationToken) =>
        catalog.GetPrintings(printingIds, cancellationToken);

    public Task<IReadOnlyList<Catalog.Contracts.CollectionVariantDetails>> GetVariants(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken) =>
        catalog.GetVariants(variantIds, cancellationToken);

    public Task<IReadOnlyList<Guid>> SearchPrintingIds(string? query, string? gameCode, CancellationToken cancellationToken) =>
        catalog.SearchPrintingIds(query, gameCode, null, cancellationToken);

    public Task<Catalog.Contracts.CollectionPrintingDetails?> GetPrinting(Guid printingId, CancellationToken cancellationToken) =>
        catalog.GetPrinting(printingId, cancellationToken);

    public Task<Catalog.Contracts.CollectionVariantDetails?> GetVariant(Guid variantId, CancellationToken cancellationToken) =>
        catalog.GetVariant(variantId, cancellationToken);
}

public sealed class MarketplaceCollectionAdapter(ICollectionQueries collection, ICollectionMarketplace commerce) : IMarketplaceCollection
{
    public async Task<MarketplaceItemIdentity?> GetItemIdentity(Guid userId, Guid entryId, CancellationToken ct)
    {
        var entry = await collection.GetEntry(userId, entryId, 1, 1, ct);
        return entry is null ? null : new(entry.PrintingId, entry.VariantId);
    }
    public Task<Collection.Contracts.CollectibleItemDto?> GetCollectibleItem(Guid userId, Guid itemId, CancellationToken cancellationToken) =>
        collection.GetItem(userId, itemId, cancellationToken);
    public Task ReserveListingItem(Domain.Listing listing, CancellationToken ct) => commerce.Reserve(
        new(listing.SellerUserId, listing.CollectibleItemId, listing.Id, listing.PrintingId, listing.VariantId, listing.Condition), ct);
    public Task ReleaseListingItem(Domain.Listing listing, CancellationToken ct) => commerce.Release(listing.SellerUserId, listing.CollectibleItemId, listing.Id, ct);
}

public sealed class MarketplaceAssetsAdapter(IAssetService assets) : IMarketplaceAssets
{
    public Task<Assets.Contracts.CollectionAssetAccess?> GetAccess(Guid userId, Guid assetId, CancellationToken cancellationToken) =>
        assets.GetCollectionAssetAccess(userId, assetId, cancellationToken);

    public Task<Assets.Contracts.CollectionAssetUrl?> GetUrl(Guid ownerId, Guid assetId, CancellationToken cancellationToken) =>
        assets.CreatePrivateReadUrl(ownerId, assetId, cancellationToken);
}
