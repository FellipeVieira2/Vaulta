using Microsoft.EntityFrameworkCore;
using Vaulta.Marketplace.Application;
using Vaulta.Marketplace.Domain;

namespace Vaulta.Marketplace.Infrastructure;

public sealed class MarketplaceStore(MarketplaceDbContext db) : IMarketplaceStore
{
    public Task<SellerProfile?> FindSellerProfile(Guid userId, CancellationToken cancellationToken) =>
        db.SellerProfiles.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

    public void AddSellerProfile(SellerProfile profile) => db.SellerProfiles.Add(profile);

    public Task<Listing?> FindListing(Guid sellerUserId, Guid listingId, CancellationToken cancellationToken) =>
        db.Listings.Include(x => x.Photos).FirstOrDefaultAsync(x => x.Id == listingId && x.SellerUserId == sellerUserId, cancellationToken);

    public Task<Listing?> FindActiveListing(Guid listingId, CancellationToken cancellationToken) =>
        db.Listings.Include(x => x.Photos).FirstOrDefaultAsync(x => x.Id == listingId && x.Status == MarketplaceRules.ActiveStatus
            && db.SellerProfiles.Any(s => s.UserId == x.SellerUserId && s.Status == MarketplaceRules.ActiveStatus), cancellationToken);

    public void AddListing(Listing listing) => db.Listings.Add(listing);
    public async Task<IReadOnlyList<Listing>> CollectionSyncListings(int offset, CancellationToken ct) =>
        await db.Listings.Where(x => x.Status == MarketplaceRules.PublishingStatus || x.Status == MarketplaceRules.CancelledStatus)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(Math.Max(0, offset)).Take(50).ToArrayAsync(ct);

    public Task Save(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
