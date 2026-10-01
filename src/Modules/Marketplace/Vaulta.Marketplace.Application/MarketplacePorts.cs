using Vaulta.Marketplace.Domain;

namespace Vaulta.Marketplace.Application;

public interface IMarketplaceStore
{
    Task<SellerProfile?> FindSellerProfile(Guid userId, CancellationToken cancellationToken);
    void AddSellerProfile(SellerProfile profile);
    Task<Listing?> FindListing(Guid sellerUserId, Guid listingId, CancellationToken cancellationToken);
    Task<Listing?> FindActiveListing(Guid listingId, CancellationToken cancellationToken);
    void AddListing(Listing listing);
    Task<IReadOnlyList<Listing>> CollectionSyncListings(int offset, CancellationToken cancellationToken);
    Task Save(CancellationToken cancellationToken);
}

public interface IMarketplaceQueries
{
    Task<Contracts.SellerProfileDto?> GetSellerProfile(Guid userId, CancellationToken cancellationToken);
    Task<Contracts.ListingPageDto> ListActiveListings(Guid? sellerUserId, Guid? printingId, Guid? variantId, int page, int pageSize, string sort, CancellationToken cancellationToken);
    Task<Contracts.ListingDto?> GetListingById(Guid listingId, CancellationToken cancellationToken);
}

public sealed record SellerReputation(decimal AverageRating, int TotalReviews);
public interface IMarketplaceReputation
{
    Task<IReadOnlyDictionary<Guid, SellerReputation>> GetSellerRatings(IReadOnlyCollection<Guid> sellerUserIds, CancellationToken cancellationToken);
}

public interface IMarketplaceCatalog
{
    Task<Catalog.Contracts.CollectionPrintingDetails?> GetPrinting(Guid printingId, CancellationToken cancellationToken);
    Task<Catalog.Contracts.CollectionVariantDetails?> GetVariant(Guid variantId, CancellationToken cancellationToken);
}

public interface IMarketplaceCollection
{
    Task<Collection.Contracts.CollectibleItemDto?> GetCollectibleItem(Guid userId, Guid itemId, CancellationToken cancellationToken);
    Task ReserveListingItem(Listing listing, CancellationToken cancellationToken);
    Task ReleaseListingItem(Listing listing, CancellationToken cancellationToken);
}

public interface IMarketplaceAssets
{
    Task<Assets.Contracts.CollectionAssetAccess?> GetAccess(Guid userId, Guid assetId, CancellationToken cancellationToken);
    Task<Assets.Contracts.CollectionAssetUrl?> GetUrl(Guid ownerId, Guid assetId, CancellationToken cancellationToken);
}
