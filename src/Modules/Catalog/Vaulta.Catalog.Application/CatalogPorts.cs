using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Application;

public interface ICatalogProvider
{
    string Code { get; }
    string? Language => null;
    Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken cancellationToken);
    Task<ProviderSetDetails> GetSetDetails(string setId, CancellationToken cancellationToken);
}

public interface ICatalogCollectionReader
{
    Task<CollectionPrintingDetails?> GetPrinting(Guid printingId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CollectionPrintingDetails>> GetPrintings(IReadOnlyCollection<Guid> printingIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<CollectionVariantDetails>> GetVariants(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> SearchPrintingIds(string? query, string? gameCode, Guid? setId, CancellationToken cancellationToken);
    Task<CollectionVariantDetails?> GetVariant(Guid variantId, CancellationToken cancellationToken);
}

public interface ICatalogSync
{
    Task<Guid> Synchronize(string provider, string scope, CancellationToken cancellationToken);
}

public interface ICatalogSearch
{
    Task<CatalogSearchPage> Search(string query, string? gameCode, int page, int pageSize, CancellationToken cancellationToken);
    Task<CatalogPrintingDetails?> GetPrinting(Guid id, CancellationToken cancellationToken);
}
