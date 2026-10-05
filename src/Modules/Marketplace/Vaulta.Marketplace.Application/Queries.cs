using Vaulta.Marketplace.Contracts;

namespace Vaulta.Marketplace.Application.Queries;

public sealed record GetSellerProfileQuery(Guid UserId);
public sealed record GetListingByIdQuery(Guid ListingId);
public sealed record ListActiveListingsQuery(Guid? SellerUserId, Guid? PrintingId, Guid? VariantId, int Page, int PageSize, string Sort);
public sealed record BrowseListingsQuery(Guid? SellerUserId, Guid? PrintingId, Guid? VariantId, string? Query, string? GameCode, int Page, int PageSize, string Sort, bool ExactVariant = false);
