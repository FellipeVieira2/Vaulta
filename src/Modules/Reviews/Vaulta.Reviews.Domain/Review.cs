using Vaulta.SharedKernel;

namespace Vaulta.Reviews.Domain;

public sealed record ReviewCreatedDomainEvent(Guid Id, Guid ReviewId, Guid OrderId, Guid ReviewerId, Guid ReviewedUserId, int Rating, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Review : AggregateRoot
{
    private Review() { }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ReviewerId { get; private set; }
    public Guid ReviewedUserId { get; private set; }
    public int Rating { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Review Create(Guid orderId, Guid reviewerId, Guid reviewedUserId, int rating, string? comment, DateTimeOffset now)
    {
        if (orderId == Guid.Empty || reviewerId == Guid.Empty || reviewedUserId == Guid.Empty)
            throw new DomainException("Order, reviewer and reviewed user identifiers are required.");
        if (reviewerId == reviewedUserId)
            throw new DomainException("Users cannot review themselves.");
        if (rating < 1 || rating > 5)
            throw new DomainException("Rating must be between 1 and 5.");

        var normalizedComment = ReviewRules.Comment(comment);

        var review = new Review
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ReviewerId = reviewerId,
            ReviewedUserId = reviewedUserId,
            Rating = rating,
            Comment = normalizedComment,
            CreatedAt = now
        };

        review.Raise(new ReviewCreatedDomainEvent(Guid.NewGuid(), review.Id, orderId, reviewerId, reviewedUserId, rating, now));
        return review;
    }
}