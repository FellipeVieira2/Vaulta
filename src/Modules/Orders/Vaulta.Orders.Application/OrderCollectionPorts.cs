using Vaulta.Orders.Domain;

namespace Vaulta.Orders.Application;

public interface IOrderCollection
{
    Task TransferDeliveredItem(Order order, CancellationToken ct);
}
public interface IOrderCollectionStore
{
    Task<IReadOnlyList<Order>> DeliveredOrders(int offset, CancellationToken ct);
}
