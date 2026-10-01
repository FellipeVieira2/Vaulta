using Microsoft.EntityFrameworkCore;
using Vaulta.Reviews.Application;
using Vaulta.Reviews.Contracts;

namespace Vaulta.Reviews.Infrastructure;

public sealed class ReviewQueries(ReviewsDbContext db) : IReviewQueries
{
    public async Task<SellerRatingDto> GetSellerRating(Guid userId, CancellationToken cancellationToken)
    {
        var summaries = await GetSellerRatings([userId], cancellationToken);
        return summaries.GetValueOrDefault(userId) ?? new(userId, 0, 0);
    }

    public async Task<IReadOnlyDictionary<Guid, SellerRatingDto>> GetSellerRatings(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, SellerRatingDto>();
        var ids = userIds.Distinct().ToArray();
        var summaries = await db.Reviews.AsNoTracking()
            .Where(x => ids.Contains(x.ReviewedUserId) && x.ReviewedRole == "SELLER")
            .GroupBy(x => x.ReviewedUserId)
            .Select(g => new { UserId = g.Key, Average = g.Average(r => (decimal)r.Rating), Total = g.Count() })
            .ToListAsync(cancellationToken);
        return summaries.ToDictionary(x => x.UserId, x => new SellerRatingDto(x.UserId, Math.Round(x.Average, 2), x.Total));
    }
}
