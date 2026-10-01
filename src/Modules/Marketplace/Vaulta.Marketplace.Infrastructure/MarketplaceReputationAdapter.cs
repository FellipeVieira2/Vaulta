using Vaulta.Marketplace.Application;
using Vaulta.Reviews.Application;

namespace Vaulta.Marketplace.Infrastructure;

public sealed class MarketplaceReputationAdapter(IReviewQueries reviews) : IMarketplaceReputation
{
    public async Task<IReadOnlyDictionary<Guid, SellerReputation>> GetSellerRatings(IReadOnlyCollection<Guid> sellerUserIds, CancellationToken cancellationToken)
    {
        var ratings = await reviews.GetSellerRatings(sellerUserIds, cancellationToken);
        return ratings.ToDictionary(x => x.Key, x => new SellerReputation(x.Value.AverageRating, x.Value.TotalReviews));
    }
}
