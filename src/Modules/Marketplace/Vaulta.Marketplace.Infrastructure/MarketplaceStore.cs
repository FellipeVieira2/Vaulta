using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    public Task<Listing?> FindDraftByKey(Guid sellerUserId, string clientDraftKey, CancellationToken ct) =>
        db.Listings.Include(x => x.Photos).SingleOrDefaultAsync(x => x.SellerUserId == sellerUserId && x.ClientDraftKey == clientDraftKey, ct);

    public async Task<Listing> CreateDraft(Listing listing, CancellationToken ct)
    {
        db.Listings.Add(listing);
        try { await db.SaveChangesAsync(ct); return listing; }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_marketplace_listing_draft_key" })
        {
            db.Entry(listing).State = EntityState.Detached;
            return await FindDraftByKey(listing.SellerUserId, listing.ClientDraftKey!, ct)
                ?? throw new InvalidOperationException("Conflicting draft was not found.");
        }
    }

    public async Task<IAsyncDisposable> LockPublication(Guid listingId, CancellationToken ct)
    {
        var key = $"marketplace-publication:{listingId:N}";
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // A session lock serializes the Collection commit and Marketplace activation without coupling their transactions.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({key}, 0))", ct);
            return new PublicationLease(db, key);
        }
        catch { await db.Database.CloseConnectionAsync(); throw; }
    }

    public Task RefreshListing(Listing listing, CancellationToken ct) => db.Entry(listing).ReloadAsync(ct);

    private sealed class PublicationLease(MarketplaceDbContext context, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({key}, 0))"); }
            finally { await context.Database.CloseConnectionAsync(); }
        }
    }
    public async Task<IReadOnlyList<Listing>> CollectionSyncListings(int offset, CancellationToken ct) =>
        await db.Listings.Where(x => x.Status == MarketplaceRules.PublishingStatus || x.Status == MarketplaceRules.CancelledStatus)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(Math.Max(0, offset)).Take(50).ToArrayAsync(ct);

    public Task Save(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
