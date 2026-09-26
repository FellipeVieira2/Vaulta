using Vaulta.Collection.Contracts;

namespace Vaulta.Collection.Application.Queries;

public sealed record GetMyCollectionQuery(Guid UserId, CollectionQuery Filter);
public sealed record GetCollectionEntryQuery(Guid UserId, Guid EntryId, int ItemsPage, int ItemsPageSize);
public sealed record GetCollectibleItemQuery(Guid UserId, Guid ItemId);
public sealed record GetCollectionSummaryQuery(Guid UserId);
