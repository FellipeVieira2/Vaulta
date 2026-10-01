using Microsoft.EntityFrameworkCore;
using Vaulta.Orders.Application;
using Vaulta.Orders.Contracts;
using Vaulta.Orders.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Orders.Infrastructure;

public sealed class OrderQueries(OrdersDbContext db) : IOrderQueries
{
    public async Task<OrderDto?> GetById(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(x => x.Id == orderId && (x.BuyerId == userId || x.SellerId == userId), cancellationToken);
        return order is null ? null : MapOrder(order);
    }

    public async Task<OrderPageDto> GetUserOrders(Guid userId, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var paging = Pagination.Normalize(page, pageSize);
        page = paging.Page;
        pageSize = paging.Size;
        var query = db.Orders.Where(x => x.BuyerId == userId || x.SellerId == userId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var totalCount = await query.CountAsync(cancellationToken);
        var orders = await query.OrderByDescending(x => x.CreatedAt)
            .Skip(paging.Offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new OrderPageDto(orders.Select(MapOrder).ToArray(), page, pageSize, totalCount);
    }

    private static OrderDto MapOrder(Order o) => new(
        o.Id, o.BuyerId, o.SellerId, o.ListingId, o.CollectibleItemId, o.PrintingId, o.VariantId,
        new OrderSnapshotDto(o.Condition, o.ItemPriceBrl, o.PlatformFeeBrl, o.TotalAmountBrl, o.Currency),
        new OrderShippingDto(o.ShippingStreet, o.ShippingNumber, o.ShippingComplement, o.ShippingNeighborhood, o.ShippingCity, o.ShippingState, o.ShippingZipCode, o.ShippingRecipient),
        o.Status, o.PaymentId, o.TrackingCode, o.CancellationReason,
        o.CreatedAt, o.UpdatedAt, o.PaidAt, o.ShippedAt, o.DeliveredAt, o.CancelledAt, o.Version);
}
