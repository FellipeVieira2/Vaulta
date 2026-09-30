using Vaulta.Marketplace.Application;
using Vaulta.Orders.Application;
using Vaulta.SharedKernel;

namespace Vaulta.Orders.Infrastructure;

public sealed class OrderMarketplaceAdapter(IMarketplaceStore store) : IOrderMarketplace
{
    public async Task<Marketplace.Contracts.ListingDto?> GetActiveListing(Guid listingId, CancellationToken cancellationToken)
    {
        var listing = await store.FindActiveListing(listingId, cancellationToken);
        if (listing is null) return null;
        return new Marketplace.Contracts.ListingDto(
            listing.Id, listing.SellerUserId, listing.CollectibleItemId, listing.PrintingId, listing.VariantId,
            listing.Condition, listing.PriceBrl, listing.Currency, listing.Status, listing.Description,
            [], listing.CreatedAt, listing.Version);
    }

    public async Task MarkListingAsSold(Guid listingId, Guid orderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var listing = await store.FindActiveListing(listingId, cancellationToken);
        if (listing is not null)
        {
            listing.MarkAsSold(orderId, now);
            await store.Save(cancellationToken);
        }
    }

    public async Task ReleaseListing(Guid listingId, CancellationToken cancellationToken)
    {
        // In a full implementation, this would restore the listing to active status.
        // For now, the listing remains in its current state since we don't have a direct
        // "unmark as sold" operation in the Listing aggregate. This is a known limitation
        // that should be addressed by adding a Reactivate() method to the Listing aggregate.
        await Task.CompletedTask;
    }
}