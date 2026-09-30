using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vaulta.Reviews.Application;
using Vaulta.Reviews.Application.Commands;
using Vaulta.Reviews.Application.Queries;
using Vaulta.Reviews.Contracts;

namespace Vaulta.Web.Api;

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this WebApplication app)
    {
        var reviews = app.MapGroup("/api/v1/reviews").WithTags("Reviews").RequireAuthorization();

        reviews.MapPost("", async (CreateReviewRequest request, ClaimsPrincipal principal,
            ReviewCommandHandlers handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new CreateReviewCommand(UserId(principal), request), ct);
            return Results.Created($"/api/v1/reviews/{result.Id}", result);
        }).WithName("CreateReview").Produces<ReviewDto>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        reviews.MapGet("/seller/{userId:guid}/rating", async (Guid userId, IReviewQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetSellerRating(userId, ct)))
            .WithName("GetSellerRating").Produces<SellerRatingDto>().ProducesProblem(401);
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);
}