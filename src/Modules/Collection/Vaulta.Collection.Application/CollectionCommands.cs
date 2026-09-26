using Vaulta.Collection.Contracts;

namespace Vaulta.Collection.Application.Commands;

public sealed record AddCollectibleItemsCommand(Guid UserId, AddCollectibleItemsRequest Request, string? IdempotencyKey);
public sealed record UpdateCollectibleItemCommand(Guid UserId, Guid ItemId, UpdateCollectibleItemRequest Request);
public sealed record RemoveCollectibleItemCommand(Guid UserId, Guid ItemId, Guid Version);
public sealed record AttachCollectibleItemAssetCommand(Guid UserId, Guid ItemId, AttachCollectibleItemAssetRequest Request);
public sealed record RemoveCollectibleItemAssetCommand(Guid UserId, Guid ItemId, Guid AssetId);
public sealed record SetPrimaryCollectibleItemAssetCommand(Guid UserId, Guid ItemId, Guid AssetId);
