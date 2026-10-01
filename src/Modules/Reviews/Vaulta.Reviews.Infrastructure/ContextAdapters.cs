using Vaulta.Orders.Application;
using Vaulta.Reviews.Application;

namespace Vaulta.Reviews.Infrastructure;

public sealed class ReviewOrdersAdapter(IOrderStore orderStore) : IReviewOrders
{
    public async Task<Orders.Contracts.OrderDto?> GetOrder(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderStore.FindOrder(orderId, cancellationToken);
        if (order is null) return null;
        return new Orders.Contracts.OrderDto(
            order.Id, order.BuyerId, order.SellerId, order.ListingId, order.CollectibleItemId,
            order.PrintingId, order.VariantId,
            new Orders.Contracts.OrderSnapshotDto(order.Condition, order.ItemPriceBrl, order.PlatformFeeBrl, order.TotalAmountBrl, order.Currency),
            new Orders.Contracts.OrderShippingDto(order.ShippingStreet, order.ShippingNumber, order.ShippingComplement, order.ShippingNeighborhood, order.ShippingCity, order.ShippingState, order.ShippingZipCode, order.ShippingRecipient),
            order.Status, order.PaymentId, order.TrackingCode, order.CancellationReason,
            order.CreatedAt, order.UpdatedAt, order.PaidAt, order.ShippedAt, order.DeliveredAt, order.CancelledAt, order.Version);
    }
}