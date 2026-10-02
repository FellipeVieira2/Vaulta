using Vaulta.Marketplace.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Marketplace.Application;

public sealed class ListingPublicationService(IMarketplaceStore store, IMarketplaceCollection collection, IClock clock,
    IMarketplaceAssets assets, IMarketplaceCatalog catalog)
{
    public async Task Publish(Listing listing, CancellationToken ct)
    {
        if (listing.Status != MarketplaceRules.PublishingStatus) return;
        await using var serialized = await store.LockPublication(listing.Id, ct);
        await store.RefreshListing(listing, ct);
        if (listing.Status != MarketplaceRules.PublishingStatus) return;
        try
        {
            if (listing.ClientDraftKey is not null) await ValidateDraft(listing, ct);
            await collection.ReserveListingItem(listing, ct);
        }
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

    private async Task ValidateDraft(Listing listing, CancellationToken ct)
    {
        var seller = await store.FindSellerProfile(listing.SellerUserId, ct);
        if (seller is null || !seller.IsActive) throw new ConflictException("Seller is no longer enabled to publish.");
        var printing = await catalog.GetPrinting(listing.PrintingId, ct);
        if (printing is null || !printing.IsActive) throw new ConflictException("Printing is no longer available in the catalog.");
        if (listing.VariantId is { } variantId)
        {
            var variant = await catalog.GetVariant(variantId, ct);
            if (variant is null || !variant.IsActive || variant.PrintingId != listing.PrintingId)
                throw new ConflictException("Variant is no longer available for this printing.");
        }
        // A durable review can outlive its assets; the worker must enforce the same photo gate as the request.
        foreach (var photo in listing.Photos)
        {
            var asset = await assets.GetAccess(listing.SellerUserId, photo.AssetId, ct);
            if (asset is null || asset.OwnerId != listing.SellerUserId || asset.Status != "ready" || asset.Visibility != "private"
                || asset.Purpose != "collection-item" || !asset.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new DomainException("Real front and back photos must remain ready and owned before publication.");
        }
    }
}
