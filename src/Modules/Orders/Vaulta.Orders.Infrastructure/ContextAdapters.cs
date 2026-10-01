using Vaulta.Marketplace.Application;
using Vaulta.Orders.Application;
using Vaulta.SharedKernel;

namespace Vaulta.Orders.Infrastructure;

public sealed class OrderMarketplaceAdapter(IMarketplaceStore store, IClock clock, IMarketplaceCollection collection) : IOrderMarketplace
{
    public async Task<Marketplace.Contracts.ListingDto?> GetActiveListing(Guid listingId, CancellationToken cancellationToken)
    {
        var listing = await store.FindActiveListing(listingId, cancellationToken);
        if (listing is null) return null;
        await collection.ReserveListingItem(listing, cancellationToken);
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
        else throw new ConflictException("Anúncio não está mais disponível para este pedido.");
    }

    public async Task ReleaseListing(Orders.Domain.Order order, CancellationToken cancellationToken)
    {
        if (order.ShippedAt.HasValue || order.Status is not (Orders.Domain.OrderRules.CancelledStatus or Orders.Domain.OrderRules.RefundedStatus))
            throw new ConflictException("Only a cancelled or fully refunded order before shipping can release its listing.");
        var listing = await store.FindListing(order.SellerId, order.ListingId, cancellationToken);
        if (listing?.Status == Marketplace.Domain.MarketplaceRules.SoldStatus && listing.SoldToOrderId == order.Id)
        {
            await collection.ReserveListingItem(listing, cancellationToken);
            if (listing.ReleaseCancelledOrder(order.Id, clock.UtcNow)) await store.Save(cancellationToken);
        }
    }
}
