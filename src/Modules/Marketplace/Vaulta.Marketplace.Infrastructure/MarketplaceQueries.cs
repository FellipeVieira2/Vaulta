using Microsoft.EntityFrameworkCore;
using Vaulta.Marketplace.Application;
using Vaulta.Marketplace.Application.Queries;
using Vaulta.Marketplace.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Marketplace.Infrastructure;

public sealed class MarketplaceQueries(MarketplaceDbContext db, IMarketplaceAssets assets, IMarketplaceReputation reputation, IMarketplaceCatalog catalog) : IMarketplaceQueries
{
    public async Task<SellerProfileDto?> GetSellerProfile(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await db.SellerProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return null;
        var ratings = await reputation.GetSellerRatings([userId], cancellationToken);
        return MarketplaceCommandHandlers.MapProfile(profile, ratings.GetValueOrDefault(userId) ?? new(0, 0));
    }
    public Task<ListingPageDto> ListActiveListings(Guid? sellerUserId, Guid? printingId, Guid? variantId, int page, int pageSize, string sort, CancellationToken cancellationToken) =>
        BrowseListings(new(sellerUserId, printingId, variantId, null, null, page, pageSize, sort), cancellationToken);

    public async Task<ListingPageDto> BrowseListings(BrowseListingsQuery request, CancellationToken cancellationToken)
    {
        var paging = Pagination.Normalize(request.Page, request.PageSize);
        var query = db.Listings.AsNoTracking().Include(x => x.Photos).Where(x => x.Status == MarketplaceRules.ActiveStatus
            && db.SellerProfiles.Any(s => s.UserId == x.SellerUserId && s.Status == MarketplaceRules.ActiveStatus));
        if (request.SellerUserId.HasValue) query = query.Where(x => x.SellerUserId == request.SellerUserId.Value);
        if (request.PrintingId.HasValue) query = query.Where(x => x.PrintingId == request.PrintingId.Value);
        if (request.ExactVariant || request.VariantId.HasValue) query = query.Where(x => x.VariantId == request.VariantId);
        if (!string.IsNullOrWhiteSpace(request.Query) || !string.IsNullOrWhiteSpace(request.GameCode))
        {
            var matchingPrintings = await catalog.SearchPrintingIds(request.Query?.Trim(), request.GameCode?.Trim(), cancellationToken);
            if (matchingPrintings.Count == 0) return new([], paging.Page, paging.Size, 0);
            query = query.Where(x => matchingPrintings.Contains(x.PrintingId));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = request.Sort?.ToLowerInvariant() switch
        {
            "price_asc" => query.OrderBy(x => x.PriceBrl).ThenBy(x => x.Id),
            "price_desc" => query.OrderByDescending(x => x.PriceBrl).ThenByDescending(x => x.Id),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        };

        var listings = await items.Skip(paging.Offset).Take(paging.Size).ToListAsync(cancellationToken);
        if (listings.Count == 0) return new([], paging.Page, paging.Size, totalCount);
        var printings = (await catalog.GetPrintings(listings.Select(x => x.PrintingId).Distinct().ToArray(), cancellationToken))
            .ToDictionary(x => x.PrintingId);
        var variantIds = listings.Where(x => x.VariantId.HasValue).Select(x => x.VariantId!.Value).Distinct().ToArray();
        var variants = variantIds.Length == 0
            ? new Dictionary<Guid, Catalog.Contracts.CollectionVariantDetails>()
            : (await catalog.GetVariants(variantIds, cancellationToken)).ToDictionary(x => x.VariantId);
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
            var printing = printings.GetValueOrDefault(listing.PrintingId);
            var variant = listing.VariantId is { } variantId ? variants.GetValueOrDefault(variantId) : null;
            var metadata = printing is null ? null : new ListingPrintingDto(printing.CardName, printing.SetName,
                printing.CollectorNumber, printing.Language, printing.GameCode, printing.ArtworkUrl,
                variant?.PrintingId == listing.PrintingId ? variant.Code : null);
            dtos.Add(new ListingDto(listing.Id, listing.SellerUserId, listing.CollectibleItemId, listing.PrintingId,
                listing.VariantId, listing.Condition, listing.PriceBrl, listing.Currency, listing.Status,
                listing.Description, photos, listing.CreatedAt, listing.Version,
                ratings.GetValueOrDefault(listing.SellerUserId)?.AverageRating ?? 0,
                ratings.GetValueOrDefault(listing.SellerUserId)?.TotalReviews ?? 0, metadata));
        }
        return new ListingPageDto(dtos, paging.Page, paging.Size, totalCount);
    }

    public async Task<ListingDto?> GetListingById(Guid listingId, CancellationToken cancellationToken)
    {
        var listing = await db.Listings.AsNoTracking().Include(x => x.Photos).FirstOrDefaultAsync(x => x.Id == listingId
            && (x.ClientDraftKey == null || x.Status == MarketplaceRules.ActiveStatus || x.Status == MarketplaceRules.SoldStatus), cancellationToken);
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
