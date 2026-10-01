using Vaulta.Marketplace.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Marketplace.Application;

public sealed class ListingPublicationService(IMarketplaceStore store, IMarketplaceCollection collection, IClock clock)
{
    public async Task Publish(Listing listing, CancellationToken ct)
    {
        if (listing.Status != MarketplaceRules.PublishingStatus) return;
        try { await collection.ReserveListingItem(listing, ct); }
        catch (Exception ex) when (ex is DomainException or ConflictException or NotFoundException)
        {
            listing.Cancel(listing.Version, clock.UtcNow);
            await store.Save(ct);
            await collection.ReleaseListingItem(listing, ct);
            throw;
        }
        listing.Publish(clock.UtcNow);
        await store.Save(ct);
    }
}
