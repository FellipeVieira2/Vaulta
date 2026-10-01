using Microsoft.EntityFrameworkCore;
using Vaulta.Collection.Application;
using Vaulta.Marketplace.Application;
using Vaulta.Marketplace.Domain;
using Vaulta.Orders.Application;
using Vaulta.Orders.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Orders.Infrastructure;

public sealed class OrderCollectionAdapter(ICollectionMarketplace collection, IMarketplaceStore marketplace) : IOrderCollection
{
    public async Task TransferDeliveredItem(Order order, CancellationToken ct)
    {
        if (order.Status != OrderRules.DeliveredStatus || !order.DeliveredAt.HasValue || !order.ShippedAt.HasValue || !order.PaidAt.HasValue)
            throw new ConflictException("Somente o recebimento confirmado pelo comprador permite transferir a carta.");
        var listing = await marketplace.FindListing(order.SellerId, order.ListingId, ct);
        if (listing is null || listing.Status != MarketplaceRules.SoldStatus || listing.SoldToOrderId != order.Id
            || listing.CollectibleItemId != order.CollectibleItemId || listing.PrintingId != order.PrintingId || listing.VariantId != order.VariantId
            || listing.Condition != order.Condition || listing.PriceBrl != order.ItemPriceBrl)
            throw new ConflictException("Pedido entregue não corresponde ao anúncio vendido.");
        await collection.Transfer(new(order.Id, order.BuyerId, new(order.SellerId, order.CollectibleItemId, order.ListingId,
            order.PrintingId, order.VariantId, order.Condition), order.ItemPriceBrl, order.DeliveredAt.Value), ct);
    }
}

public sealed class OrderCollectionStore(OrdersDbContext db) : IOrderCollectionStore
{
    public async Task<IReadOnlyList<Order>> DeliveredOrders(int offset, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Where(x => x.Status == OrderRules.DeliveredStatus)
            .OrderBy(x => x.DeliveredAt).ThenBy(x => x.Id).Skip(Math.Max(0, offset)).Take(50).ToArrayAsync(ct);
}
