using Microsoft.EntityFrameworkCore;
using Vaulta.Reviews.Application;
using Vaulta.Reviews.Contracts;

namespace Vaulta.Reviews.Infrastructure;

public sealed class ReviewQueries(ReviewsDbContext db) : IReviewQueries
{
    public async Task<SellerRatingDto> GetSellerRating(Guid userId, CancellationToken cancellationToken)
    {
        var reviews = await db.Reviews.Where(x => x.ReviewedUserId == userId).ToListAsync(cancellationToken);
        var total = reviews.Count;
        var average = total == 0 ? 0m : Math.Round((decimal)reviews.Average(r => r.Rating), 2);
        return new SellerRatingDto(userId, average, total);
    }
}