using Vaulta.Collection.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Collection.Application;

public sealed record ListedCollectionItem(Guid SellerId, Guid ItemId, Guid ListingId, Guid PrintingId, Guid? VariantId, string Condition);
public sealed record DeliveredCollectionItem(Guid OrderId, Guid BuyerId, ListedCollectionItem Listing, decimal PriceBrl, DateTimeOffset ReceivedAt);
public interface ICollectionMarketplace
{
    Task Reserve(ListedCollectionItem listing, CancellationToken ct);
    Task Release(Guid sellerId, Guid itemId, Guid listingId, CancellationToken ct);
    Task Transfer(DeliveredCollectionItem delivery, CancellationToken ct);
}
public interface ICollectionCommerceStore
{
    Task<CollectionEntry?> FindEntryById(Guid userId, Guid entryId, CancellationToken ct);
    Task<ItemOwnershipTransfer?> FindTransfer(Guid orderId, CancellationToken ct);
    void AddTransfer(ItemOwnershipTransfer transfer);
}

public sealed class CollectionMarketplaceService(ICollectionStore store, ICollectionCommerceStore commerce, IClock clock) : ICollectionMarketplace
{
    public async Task Reserve(ListedCollectionItem listing, CancellationToken ct)
    {
        var item = await RequireListedItem(listing, ct);
        item.ReserveForListing(listing.ListingId, clock.UtcNow);
        await store.Save(ct);
    }

    public async Task Release(Guid sellerId, Guid itemId, Guid listingId, CancellationToken ct)
    {
        var item = await store.FindItem(sellerId, itemId, ct);
        if (item is null) return;
        item.ReleaseListing(listingId, clock.UtcNow);
        await store.Save(ct);
    }

    public async Task Transfer(DeliveredCollectionItem delivery, CancellationToken ct)
    {
        if (delivery.OrderId == Guid.Empty || delivery.BuyerId == delivery.Listing.SellerId || delivery.BuyerId == Guid.Empty)
            throw new DomainException("Invalid delivery participants.");
        // Serialize against collection additions and other transfers into this entry.
        await store.LockEntryIdentity(delivery.BuyerId, delivery.Listing.PrintingId, delivery.Listing.VariantId, ct);
        var existing = await commerce.FindTransfer(delivery.OrderId, ct);
        if (existing is not null)
        {
            if (existing.ListingId != delivery.Listing.ListingId || existing.SellerItemId != delivery.Listing.ItemId
                || existing.SellerId != delivery.Listing.SellerId || existing.BuyerId != delivery.BuyerId || existing.PriceBrl != delivery.PriceBrl)
                throw new ConflictException("Delivered order does not match the recorded transfer.");
            // End the advisory-lock transaction even for an idempotent replay.
            await store.Save(ct);
            return;
        }
        var source = await RequireListedItem(delivery.Listing, ct);
        if (source.ListedById != delivery.Listing.ListingId)
            throw new ConflictException("The unit is not reserved by this listing.");
        var entry = await store.FindEntry(delivery.BuyerId, delivery.Listing.PrintingId, delivery.Listing.VariantId, ct);
        if (entry is null)
        {
            entry = CollectionEntry.Create(delivery.BuyerId, delivery.Listing.PrintingId, delivery.Listing.VariantId, clock.UtcNow);
            store.AddEntry(entry);
        }
        var items = entry.AddItems(1, source.Condition, new Money(delivery.PriceBrl, "BRL"), DateOnly.FromDateTime(delivery.ReceivedAt.UtcDateTime), null, clock.UtcNow);
        var buyerItem = items.Single();
        source.RecordSale(delivery.Listing.ListingId, delivery.OrderId, buyerItem.Id, clock.UtcNow);
        store.AddItems(items);
        commerce.AddTransfer(ItemOwnershipTransfer.Create(delivery.OrderId, delivery.Listing.ListingId, delivery.Listing.SellerId,
            delivery.BuyerId, source.Id, buyerItem.Id, delivery.PriceBrl, delivery.ReceivedAt));
        await store.Save(ct);
    }

    private async Task<CollectibleItem> RequireListedItem(ListedCollectionItem listing, CancellationToken ct)
    {
        var item = await store.FindItem(listing.SellerId, listing.ItemId, ct)
            ?? throw new ConflictException("A unidade anunciada não pertence à coleção do vendedor.");
        var entry = await commerce.FindEntryById(listing.SellerId, item.CollectionEntryId, ct);
        if (entry is null || entry.PrintingId != listing.PrintingId || entry.VariantId != listing.VariantId
            || item.Condition != CollectionRules.Condition(listing.Condition) || !item.IsActive)
            throw new ConflictException("Carta, variante ou condição do anúncio não corresponde à unidade da coleção.");
        return item;
    }
}
