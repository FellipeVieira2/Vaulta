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
        marketplace.MapGet("/product-filters", async (string? game, string? setQuery, int? page, int? pageSize,
            Vaulta.Catalog.Application.ICatalogMarketplaceReader catalog, CancellationToken ct) =>
        {
            var options = await catalog.GetFilterOptions(game,setQuery,page ?? 1,pageSize ?? 20,ct);
            return Results.Ok(new MarketplaceProductFilterOptionsDto(options.Sets.Select(x=>new MarketplaceSetOptionDto(x.Id,x.Name)).ToArray(),
                options.Page,options.PageSize,options.TotalCount,options.Languages,options.VariantCodes));
        }).WithName("GetMarketplaceProductFilters").Produces<MarketplaceProductFilterOptionsDto>().ProducesProblem(400);
        marketplace.MapGet("/products", async (string? query, string? game, Guid? setId, string? language,
            string? variantCode, string? condition, decimal? minPriceBrl, decimal? maxPriceBrl, bool? photosOnly,
            int? page, int? pageSize, string? sort, IMarketplaceProductQueries products, CancellationToken ct) =>
            Results.Ok(await products.BrowseProducts(new(query, game, page ?? 1, pageSize ?? 20, sort ?? "newest",
                setId, language, variantCode, condition, minPriceBrl, maxPriceBrl, photosOnly ?? false), ct)))
            .WithName("BrowseMarketplaceProducts").Produces<MarketplaceProductPageDto>().ProducesProblem(400);
        marketplace.MapGet("/products/{printingId:guid}/variants/{variantKey}", async (Guid printingId,
            string variantKey, IMarketplaceProductQueries products, CancellationToken ct) =>
        {
            var product = await products.GetProduct(printingId, VariantIdentity(variantKey), ct);
            return product is null ? Results.NotFound() : Results.Ok(product);
        }).WithName("GetMarketplaceProduct").Produces<MarketplaceProductDto>().ProducesProblem(400).ProducesProblem(404);
        marketplace.MapGet("/products/{printingId:guid}/variants/{variantKey}/offers", async (Guid printingId,
            string variantKey, int? page, int? pageSize, string? sort, IMarketplaceProductQueries products, CancellationToken ct) =>
        {
            var offers = await products.GetOffers(printingId, VariantIdentity(variantKey), page ?? 1, pageSize ?? 20, sort ?? "price_asc", ct);
            return offers is null ? Results.NotFound() : Results.Ok(offers);
        }).WithName("GetMarketplaceProductOffers").Produces<ListingPageDto>().ProducesProblem(400).ProducesProblem(404);
        var mySeller = app.MapGroup("/api/v1/me/seller").WithTags("Marketplace").RequireAuthorization();
        var drafts = mySeller.MapGroup("/listing-drafts");

        drafts.MapPost("", async (CreateListingDraftRequest request, ClaimsPrincipal principal, ListingDraftHandlers handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new CreateListingDraftCommand(UserId(principal), request), ct);
            return Results.Created($"/api/v1/me/seller/listing-drafts/{result.Id}", result);
        }).WithName("CreateListingDraft").Produces<ListingDraftDto>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        drafts.MapGet("/{draftId:guid}", async (Guid draftId, ClaimsPrincipal principal, ListingDraftHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Get(UserId(principal), draftId, ct)))
            .WithName("GetListingDraft").Produces<ListingDraftDto>().ProducesProblem(401).ProducesProblem(404);

        drafts.MapPut("/{draftId:guid}", async (Guid draftId, UpdateListingDraftRequest request, ClaimsPrincipal principal, ListingDraftHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new UpdateListingDraftCommand(UserId(principal), draftId, request), ct)))
            .WithName("UpdateListingDraft").Produces<ListingDraftDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        drafts.MapPost("/{draftId:guid}/photos", async (Guid draftId, ListingDraftPhotoRequest request, ClaimsPrincipal principal, ListingDraftHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new AddListingDraftPhotoCommand(UserId(principal), draftId, request), ct)))
            .WithName("AddListingDraftPhoto").Produces<ListingDraftDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        drafts.MapDelete("/{draftId:guid}/photos/{assetId:guid}", async (Guid draftId, Guid assetId, Guid version, ClaimsPrincipal principal, ListingDraftHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new RemoveListingDraftPhotoCommand(UserId(principal), draftId, assetId, version), ct)))
            .WithName("RemoveListingDraftPhoto").Produces<ListingDraftDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        drafts.MapPost("/{draftId:guid}/publish", async (Guid draftId, PublishListingDraftRequest request, ClaimsPrincipal principal, ListingDraftHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new PublishListingDraftCommand(UserId(principal), draftId, request), ct)))
            .WithName("PublishListingDraft").Produces<ListingDraftDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        drafts.MapDelete("/{draftId:guid}", async (Guid draftId, Guid version, ClaimsPrincipal principal, ListingDraftHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new CancelListingDraftCommand(UserId(principal), draftId, version), ct)))
            .WithName("CancelListingDraft").Produces<ListingDraftDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        // Public: browse active listings
        marketplace.MapGet("/listings", async (Guid? sellerUserId, Guid? printingId, Guid? variantId,
            int? page, int? pageSize, string? sort, string? query, string? game, IMarketplaceQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.BrowseListings(new(sellerUserId, printingId, variantId, query, game,
                page ?? 1, pageSize ?? 20, sort ?? "newest"), ct)))
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

    private static Guid? VariantIdentity(string key) => key == "none" ? null
        : Guid.TryParse(key, out var id) && id != Guid.Empty ? id
        : throw new Vaulta.SharedKernel.DomainException("Use a variant ID or the explicit none identity.");
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);
}
