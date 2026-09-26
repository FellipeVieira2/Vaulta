using Vaulta.Assets.Contracts;
using Vaulta.Catalog.Contracts;
using Vaulta.Collection.Contracts;
using Vaulta.Collection.Domain;

namespace Vaulta.Collection.Application;

public interface ICollectionStore
{
    Task LockEntryIdentity(Guid userId, Guid printingId, Guid? variantId, CancellationToken cancellationToken);
    Task<CollectionEntry?> FindEntry(Guid userId, Guid printingId, Guid? variantId, CancellationToken cancellationToken);
    void AddEntry(CollectionEntry entry);
    void AddItems(IEnumerable<CollectibleItem> items);
    Task<CollectibleItem?> FindItem(Guid userId, Guid itemId, CancellationToken cancellationToken);
    Task Save(CancellationToken cancellationToken);
}

public interface ICollectionQueries
{
    Task<CollectionPageDto> GetMyCollection(Guid userId, CollectionQuery query, CancellationToken cancellationToken);
    Task<CollectionEntryDetailsDto?> GetEntry(Guid userId, Guid entryId, int page, int pageSize, CancellationToken cancellationToken);
    Task<CollectibleItemDto?> GetItem(Guid userId, Guid itemId, CancellationToken cancellationToken);
    Task<CollectionSummaryDto> GetSummary(Guid userId, CancellationToken cancellationToken);
}

public interface ICollectionCatalog
{
    Task<CollectionPrintingDetails?> GetPrinting(Guid printingId, CancellationToken cancellationToken);
    Task<CollectionVariantDetails?> GetVariant(Guid variantId, CancellationToken cancellationToken);
}

public interface ICollectionAssets
{
    Task<CollectionAssetAccess?> GetAccess(Guid ownerId, Guid assetId, CancellationToken cancellationToken);
    Task<CollectionAssetUrl?> GetPrivateUrl(Guid ownerId, Guid assetId, CancellationToken cancellationToken);
}
