using Vaulta.Reviews.Domain;

namespace Vaulta.Reviews.Application;

public interface IReviewStore
{
    Task<bool> HasReviewed(Guid orderId, Guid reviewerId, CancellationToken cancellationToken);
    void Add(Review review);
    Task Save(CancellationToken cancellationToken);
}

public interface IReviewQueries
{
    Task<Contracts.SellerRatingDto> GetSellerRating(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, Contracts.SellerRatingDto>> GetSellerRatings(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

public interface IReviewOrders
{
    Task<Orders.Contracts.OrderDto?> GetOrder(Guid orderId, CancellationToken cancellationToken);
}
