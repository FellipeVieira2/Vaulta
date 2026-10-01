using FluentValidation;
using Vaulta.Orders.Domain;
using Vaulta.Reviews.Application.Commands;
using Vaulta.Reviews.Contracts;
using Vaulta.Reviews.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Reviews.Application;

public sealed class ReviewCommandHandlers(
    IReviewStore store,
    IReviewOrders orders,
    IClock clock)
{
    public async Task<ReviewDto> Handle(CreateReviewCommand command, CancellationToken cancellationToken)
    {
        await new CreateReviewValidator().ValidateAndThrowAsync(command.Request, cancellationToken);

        var order = await orders.GetOrder(command.Request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order not found.");

        if (order.BuyerId != command.UserId && order.SellerId != command.UserId)
            throw new ForbiddenException("Only the buyer or seller can review this order.");

        if (order.Status != OrderRules.DeliveredStatus)
            throw new ConflictException($"Reviews can only be created for delivered orders. Current status: {order.Status}");

        var reviewerId = command.UserId;
        var reviewedUserId = reviewerId == order.BuyerId ? order.SellerId : order.BuyerId;

        if (await store.HasReviewed(order.Id, reviewerId, cancellationToken))
            throw new ConflictException("You have already reviewed this order.");

        var now = clock.UtcNow;
        var reviewedRole = reviewerId == order.BuyerId ? "SELLER" : "BUYER";
        var review = Review.Create(order.Id, reviewerId, reviewedUserId, reviewedRole, command.Request.Rating, command.Request.Comment, now);

        store.Add(review);
        await store.Save(cancellationToken);

        return new ReviewDto(review.Id, review.OrderId, review.ReviewerId, review.ReviewedUserId, review.Rating, review.Comment, review.CreatedAt, review.ReviewedRole);
    }
}

public sealed class CreateReviewValidator : AbstractValidator<CreateReviewRequest>
{
    public CreateReviewValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween(ReviewRules.MinRating, ReviewRules.MaxRating);
        RuleFor(x => x.Comment).MaximumLength(ReviewRules.MaxCommentLength);
    }
}
