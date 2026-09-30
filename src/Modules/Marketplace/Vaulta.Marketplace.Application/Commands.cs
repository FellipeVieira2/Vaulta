using Vaulta.Marketplace.Contracts;

namespace Vaulta.Marketplace.Application.Commands;

public sealed record EnableSellerCommand(Guid UserId, SellerProfileRequest Request);
public sealed record UpdateSellerProfileCommand(Guid UserId, SellerProfileRequest Request, Guid Version);
public sealed record CreateListingCommand(Guid UserId, CreateListingRequest Request);
public sealed record UpdateListingCommand(Guid UserId, Guid ListingId, UpdateListingRequest Request);
public sealed record CancelListingCommand(Guid UserId, Guid ListingId, Guid Version);
public sealed record AddListingPhotoCommand(Guid UserId, Guid ListingId, ListingPhotoRequest Request);
public sealed record RemoveListingPhotoCommand(Guid UserId, Guid ListingId, Guid AssetId);