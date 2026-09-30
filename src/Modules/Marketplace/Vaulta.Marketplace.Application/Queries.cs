using Vaulta.Marketplace.Contracts;

namespace Vaulta.Marketplace.Application.Queries;

public sealed record GetSellerProfileQuery(Guid UserId);
public sealed record GetListingByIdQuery(Guid ListingId);
public sealed record ListActiveListingsQuery(Guid? SellerUserId, Guid? PrintingId, Guid? VariantId, int Page, int PageSize, string Sort);