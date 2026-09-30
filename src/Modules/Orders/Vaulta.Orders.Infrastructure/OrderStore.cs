using Microsoft.EntityFrameworkCore;
using Vaulta.Orders.Application;
using Vaulta.Orders.Domain;

namespace Vaulta.Orders.Infrastructure;

public sealed class OrderStore(OrdersDbContext db) : IOrderStore
{
    public Task<Reservation?> FindActiveReservation(Guid listingId, CancellationToken cancellationToken) =>
        db.Reservations.FirstOrDefaultAsync(x => x.ListingId == listingId && x.Status == OrderRules.ReservationActiveStatus && x.ReservedUntil > DateTimeOffset.UtcNow, cancellationToken);

    public Task<Reservation?> FindReservationByBuyerAndListing(Guid buyerId, Guid listingId, CancellationToken cancellationToken) =>
        db.Reservations.FirstOrDefaultAsync(x => x.BuyerId == buyerId && x.ListingId == listingId && x.Status == OrderRules.ReservationActiveStatus && x.ReservedUntil > DateTimeOffset.UtcNow, cancellationToken);

    public void AddReservation(Reservation reservation) => db.Reservations.Add(reservation);

    public Task<Order?> FindOrder(Guid orderId, CancellationToken cancellationToken) =>
        db.Orders.FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken);

    public Task<Order?> FindOrderForUser(Guid userId, Guid orderId, CancellationToken cancellationToken) =>
        db.Orders.FirstOrDefaultAsync(x => x.Id == orderId && (x.BuyerId == userId || x.SellerId == userId), cancellationToken);

    public void AddOrder(Order order) => db.Orders.Add(order);

    public Task Save(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}