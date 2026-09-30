namespace Vaulta.Reviews.Contracts;

public sealed record CreateReviewRequest(Guid OrderId, int Rating, string? Comment);
public sealed record ReviewDto(Guid Id, Guid OrderId, Guid ReviewerId, Guid ReviewedUserId, int Rating, string? Comment, DateTimeOffset CreatedAt);
public sealed record SellerRatingDto(Guid UserId, decimal AverageRating, int TotalReviews);