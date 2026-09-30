using Microsoft.EntityFrameworkCore;
using Vaulta.Shipping.Application;
using Vaulta.Shipping.Domain;

namespace Vaulta.Shipping.Infrastructure;

public sealed class ShippingStore(ShippingDbContext db) : IShippingStore
{
    public Task<Shipment?> FindByOrderId(Guid orderId, CancellationToken cancellationToken) =>
        db.Shipments.Include(x => x.Events).FirstOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

    public Task<Shipment?> FindById(Guid shipmentId, CancellationToken cancellationToken) =>
        db.Shipments.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == shipmentId, cancellationToken);

    public void Add(Shipment shipment) => db.Shipments.Add(shipment);

    public Task Save(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}