using Microsoft.EntityFrameworkCore;
using Vaulta.Marketplace.Application;
using Vaulta.Marketplace.Contracts;
using Vaulta.Marketplace.Domain;

namespace Vaulta.Marketplace.Infrastructure;

public sealed class MarketplaceQueries(MarketplaceDbContext db, IMarketplaceAssets assets) : IMarketplaceQueries
{
    public async Task<ListingPageDto> ListActiveListings(Guid? sellerUserId, Guid? printingId, Guid? variantId, int page, int pageSize, string sort, CancellationToken cancellationToken)
    {
        var query = db.Listings.Include(x => x.Photos).Where(x => x.Status == MarketplaceRules.ActiveStatus);
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

        var listings = await items.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var dtos = new List<ListingDto>();
        foreach (var listing in listings)
        {
            var photos = new List<ListingPhotoDto>();
            foreach (var photo in listing.Photos.OrderBy(p => p.SortOrder))
            {
                var url = await assets.GetUrl(photo.AssetId, cancellationToken);
                photos.Add(new ListingPhotoDto(photo.AssetId, photo.Type, photo.SortOrder, photo.IsPrimary,
                    url?.Url ?? "", url?.ExpiresAt ?? DateTimeOffset.MinValue));
            }
            dtos.Add(new ListingDto(listing.Id, listing.SellerUserId, listing.CollectibleItemId, listing.PrintingId,
                listing.VariantId, listing.Condition, listing.PriceBrl, listing.Currency, listing.Status,
                listing.Description, photos, listing.CreatedAt, listing.Version));
        }
        return new ListingPageDto(dtos, page, pageSize, totalCount);
    }

    public async Task<ListingDto?> GetListingById(Guid listingId, CancellationToken cancellationToken)
    {
        var listing = await db.Listings.Include(x => x.Photos).FirstOrDefaultAsync(x => x.Id == listingId, cancellationToken);
        if (listing is null) return null;
        var photos = new List<ListingPhotoDto>();
        foreach (var photo in listing.Photos.OrderBy(p => p.SortOrder))
        {
            var url = await assets.GetUrl(photo.AssetId, cancellationToken);
            photos.Add(new ListingPhotoDto(photo.AssetId, photo.Type, photo.SortOrder, photo.IsPrimary,
                url?.Url ?? "", url?.ExpiresAt ?? DateTimeOffset.MinValue));
        }
        return new ListingDto(listing.Id, listing.SellerUserId, listing.CollectibleItemId, listing.PrintingId,
            listing.VariantId, listing.Condition, listing.PriceBrl, listing.Currency, listing.Status,
            listing.Description, photos, listing.CreatedAt, listing.Version);
    }
}