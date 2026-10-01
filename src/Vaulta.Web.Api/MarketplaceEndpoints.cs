using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vaulta.Marketplace.Application;
using Vaulta.Marketplace.Application.Commands;
using Vaulta.Marketplace.Application.Queries;
using Vaulta.Marketplace.Contracts;

namespace Vaulta.Web.Api;

public static class MarketplaceEndpoints
{
    public static void MapMarketplaceEndpoints(this WebApplication app)
    {
        var marketplace = app.MapGroup("/api/v1/marketplace").WithTags("Marketplace");
        var mySeller = app.MapGroup("/api/v1/me/seller").WithTags("Marketplace").RequireAuthorization();

        // Public: browse active listings
        marketplace.MapGet("/listings", async (Guid? sellerUserId, Guid? printingId, Guid? variantId,
            int? page, int? pageSize, string? sort, IMarketplaceQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.ListActiveListings(sellerUserId, printingId, variantId,
                page ?? 1, pageSize ?? 20, sort ?? "newest", ct)))
            .WithName("ListActiveListings").Produces<ListingPageDto>().ProducesProblem(400);

        marketplace.MapGet("/listings/{listingId:guid}", async (Guid listingId, IMarketplaceQueries queries, CancellationToken ct) =>
        {
            var listing = await queries.GetListingById(listingId, ct);
            return listing is null ? Results.NotFound() : Results.Ok(listing);
        }).WithName("GetListingById").Produces<ListingDto>().ProducesProblem(404);

        // Seller profile management
        mySeller.MapGet("", async (ClaimsPrincipal principal, IMarketplaceQueries queries, CancellationToken ct) =>
        {
            var userId = UserId(principal);
            var profile = await queries.GetSellerProfile(userId, ct);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        }).WithName("GetMySellerProfile").Produces<SellerProfileDto>().ProducesProblem(401).ProducesProblem(404);

        mySeller.MapPost("", async (SellerProfileRequest request, ClaimsPrincipal principal,
            MarketplaceCommandHandlers handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new EnableSellerCommand(UserId(principal), request), ct);
            return Results.Created($"/api/v1/me/seller", result);
        }).WithName("EnableSeller").Produces<SellerProfileDto>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);

        mySeller.MapPut("", async (SellerProfileRequest request, Guid version, ClaimsPrincipal principal,
            MarketplaceCommandHandlers handler, CancellationToken ct) =>
        {
            var newVersion = await handler.Handle(new UpdateSellerProfileCommand(UserId(principal), request, version), ct);
            return Results.Ok(new { version = newVersion });
        }).WithName("UpdateSellerProfile").Produces(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        // Listing CRUD
        mySeller.MapPost("/listings", async (CreateListingRequest request, ClaimsPrincipal principal,
            MarketplaceCommandHandlers handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new CreateListingCommand(UserId(principal), request), ct);
            return Results.Created($"/api/v1/marketplace/listings/{result.Id}", result);
        }).WithName("CreateListing").Produces<ListingDto>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        mySeller.MapPut("/listings/{listingId:guid}", async (Guid listingId, UpdateListingRequest request,
            ClaimsPrincipal principal, MarketplaceCommandHandlers handler, CancellationToken ct) =>
        {
            var newVersion = await handler.Handle(new UpdateListingCommand(UserId(principal), listingId, request), ct);
            return Results.Ok(new { version = newVersion });
        }).WithName("UpdateListing").Produces(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        mySeller.MapDelete("/listings/{listingId:guid}", async (Guid listingId, Guid version,
            ClaimsPrincipal principal, MarketplaceCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new CancelListingCommand(UserId(principal), listingId, version), ct);
            return Results.NoContent();
        }).WithName("CancelListing").Produces(204).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        // Listing photos
        mySeller.MapPost("/listings/{listingId:guid}/photos", async (Guid listingId, ListingPhotoRequest request,
            ClaimsPrincipal principal, MarketplaceCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new AddListingPhotoCommand(UserId(principal), listingId, request), ct);
            return Results.NoContent();
        }).WithName("AddListingPhoto").Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        mySeller.MapDelete("/listings/{listingId:guid}/photos/{assetId:guid}", async (Guid listingId, Guid assetId,
            ClaimsPrincipal principal, MarketplaceCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new RemoveListingPhotoCommand(UserId(principal), listingId, assetId), ct);
            return Results.NoContent();
        }).WithName("RemoveListingPhoto").Produces(204).ProducesProblem(401).ProducesProblem(404);
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);
}
