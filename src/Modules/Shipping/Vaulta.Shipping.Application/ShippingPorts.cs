using Vaulta.Shipping.Domain;

namespace Vaulta.Shipping.Application;

public interface IShippingStore
{
    Task<Shipment?> FindByOrderId(Guid orderId, CancellationToken cancellationToken);
    Task<Shipment?> FindById(Guid shipmentId, CancellationToken cancellationToken);
    void Add(Shipment shipment);
    Task Save(CancellationToken cancellationToken);
}

public interface IShippingOrders
{
    Task<Orders.Contracts.OrderDto?> GetOrder(Guid orderId, CancellationToken cancellationToken);
    Task MarkOrderAsShipped(Guid orderId, string trackingCode, CancellationToken cancellationToken);
}
