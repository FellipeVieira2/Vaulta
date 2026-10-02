using Vaulta.Marketplace.Contracts;

namespace Vaulta.Marketplace.Application.Commands;

public sealed record CreateListingDraftCommand(Guid UserId, CreateListingDraftRequest Request);
public sealed record UpdateListingDraftCommand(Guid UserId, Guid DraftId, UpdateListingDraftRequest Request);
public sealed record AddListingDraftPhotoCommand(Guid UserId, Guid DraftId, ListingDraftPhotoRequest Request);
public sealed record RemoveListingDraftPhotoCommand(Guid UserId, Guid DraftId, Guid AssetId, Guid Version);
public sealed record PublishListingDraftCommand(Guid UserId, Guid DraftId, PublishListingDraftRequest Request);
public sealed record CancelListingDraftCommand(Guid UserId, Guid DraftId, Guid Version);
