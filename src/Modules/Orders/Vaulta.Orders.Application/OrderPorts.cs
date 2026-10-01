using Vaulta.Orders.Domain;

namespace Vaulta.Orders.Application;

public interface IOrderStore
{
    Task<Reservation?> FindActiveReservation(Guid listingId, CancellationToken cancellationToken);
    Task<Reservation?> FindReservationByBuyerAndListing(Guid buyerId, Guid listingId, CancellationToken cancellationToken);
    void AddReservation(Reservation reservation);
    Task<Order?> FindOrder(Guid orderId, CancellationToken cancellationToken);
    Task<Order?> FindOrderForUser(Guid userId, Guid orderId, CancellationToken cancellationToken);
    void AddOrder(Order order);
    Task<IReadOnlyList<Order>> ReleasableOrders(int offset, CancellationToken cancellationToken);
    Task Save(CancellationToken cancellationToken);
}

public interface IOrderQueries
{
    Task<Contracts.OrderDto?> GetById(Guid userId, Guid orderId, CancellationToken cancellationToken);
    Task<Contracts.OrderPageDto> GetUserOrders(Guid userId, string? status, int page, int pageSize, CancellationToken cancellationToken);
}

public interface IOrderMarketplace
{
    Task<Marketplace.Contracts.ListingDto?> GetActiveListing(Guid listingId, CancellationToken cancellationToken);
    Task MarkListingAsSold(Guid listingId, Guid orderId, DateTimeOffset now, CancellationToken cancellationToken);
    Task ReleaseListing(Order order, CancellationToken cancellationToken);
}
