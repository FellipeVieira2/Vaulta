using Microsoft.EntityFrameworkCore;
using Vaulta.Reviews.Application;
using Vaulta.Reviews.Domain;

namespace Vaulta.Reviews.Infrastructure;

public sealed class ReviewStore(ReviewsDbContext db) : IReviewStore
{
    public Task<bool> HasReviewed(Guid orderId, Guid reviewerId, CancellationToken cancellationToken) =>
        db.Reviews.AnyAsync(x => x.OrderId == orderId && x.ReviewerId == reviewerId, cancellationToken);

    public void Add(Review review) => db.Reviews.Add(review);

    public Task Save(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}