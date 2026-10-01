using Microsoft.EntityFrameworkCore;
using Vaulta.Marketplace.Application;
using Vaulta.Marketplace.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Marketplace.Infrastructure;

public sealed class MarketplaceQueries(MarketplaceDbContext db, IMarketplaceAssets assets, IMarketplaceReputation reputation) : IMarketplaceQueries
{
    public async Task<SellerProfileDto?> GetSellerProfile(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await db.SellerProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return null;
        var ratings = await reputation.GetSellerRatings([userId], cancellationToken);
        return MarketplaceCommandHandlers.MapProfile(profile, ratings.GetValueOrDefault(userId) ?? new(0, 0));
    }
    public async Task<ListingPageDto> ListActiveListings(Guid? sellerUserId, Guid? printingId, Guid? variantId, int page, int pageSize, string sort, CancellationToken cancellationToken)
    {
        var paging = Pagination.Normalize(page, pageSize);
        page = paging.Page;
        pageSize = paging.Size;
        var query = db.Listings.Include(x => x.Photos).Where(x => x.Status == MarketplaceRules.ActiveStatus
            && db.SellerProfiles.Any(s => s.UserId == x.SellerUserId && s.Status == MarketplaceRules.ActiveStatus));
        if (sellerUserId.HasValue) query = query.Where(x => x.SellerUserId == sellerUserId.Value);
        if (printingId.HasValue) query = query.Where(x => x.PrintingId == printingId.Value);
        if (variantId.HasValue) query = query.Where(x => x.VariantId == variantId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = sort?.ToLowerInvariant() switch
        {
            "price_asc" => query.OrderBy(x => x.PriceBrl),
            "price_desc" => query.OrderByDescending(x => x.PriceBrl),
            "newest" => query.OrderByDescending(x => x.CreatedAt),
            _ => query.OrderByDescending(x => x.CreatedAt)
        };

        var listings = await items.Skip(paging.Offset).Take(pageSize).ToListAsync(cancellationToken);
        var ratings = await reputation.GetSellerRatings(listings.Select(x => x.SellerUserId).Distinct().ToArray(), cancellationToken);
        var dtos = new List<ListingDto>();
        foreach (var listing in listings)
        {
            var photos = new List<ListingPhotoDto>();
            foreach (var photo in listing.Photos.OrderBy(p => p.SortOrder))
            {
                var url = await assets.GetUrl(listing.SellerUserId, photo.AssetId, cancellationToken);
                photos.Add(new ListingPhotoDto(photo.AssetId, photo.Type, photo.SortOrder, photo.IsPrimary,
                    url?.Url ?? "", url?.ExpiresAt ?? DateTimeOffset.MinValue));
            }
            dtos.Add(new ListingDto(listing.Id, listing.SellerUserId, listing.CollectibleItemId, listing.PrintingId,
                listing.VariantId, listing.Condition, listing.PriceBrl, listing.Currency, listing.Status,
                listing.Description, photos, listing.CreatedAt, listing.Version,
                ratings.GetValueOrDefault(listing.SellerUserId)?.AverageRating ?? 0,
                ratings.GetValueOrDefault(listing.SellerUserId)?.TotalReviews ?? 0));
        }
        return new ListingPageDto(dtos, page, pageSize, totalCount);
    }

    public async Task<ListingDto?> GetListingById(Guid listingId, CancellationToken cancellationToken)
    {
        var listing = await db.Listings.Include(x => x.Photos).FirstOrDefaultAsync(x => x.Id == listingId, cancellationToken);
        if (listing is null) return null;
        var ratings = await reputation.GetSellerRatings([listing.SellerUserId], cancellationToken);
        var photos = new List<ListingPhotoDto>();
        foreach (var photo in listing.Photos.OrderBy(p => p.SortOrder))
        {
            var url = await assets.GetUrl(listing.SellerUserId, photo.AssetId, cancellationToken);
            photos.Add(new ListingPhotoDto(photo.AssetId, photo.Type, photo.SortOrder, photo.IsPrimary,
                url?.Url ?? "", url?.ExpiresAt ?? DateTimeOffset.MinValue));
        }
        return new ListingDto(listing.Id, listing.SellerUserId, listing.CollectibleItemId, listing.PrintingId,
            listing.VariantId, listing.Condition, listing.PriceBrl, listing.Currency, listing.Status,
            listing.Description, photos, listing.CreatedAt, listing.Version,
            ratings.GetValueOrDefault(listing.SellerUserId)?.AverageRating ?? 0,
            ratings.GetValueOrDefault(listing.SellerUserId)?.TotalReviews ?? 0);
    }
}
