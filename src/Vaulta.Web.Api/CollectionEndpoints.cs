using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vaulta.Collection.Application;
using Vaulta.Collection.Application.Commands;
using Vaulta.Collection.Application.Queries;
using Vaulta.Collection.Contracts;

namespace Vaulta.Web.Api;

public static class CollectionEndpoints
{
    public static void MapCollectionEndpoints(this WebApplication app)
    {
        var collection = app.MapGroup("/api/v1/me/collection").WithTags("Collection").RequireAuthorization();

        collection.MapGet("", async (string? query, string? game, Guid? setId, string? condition, Guid? variantId,
            int? page, int? pageSize, string? sort, ClaimsPrincipal principal, CollectionQueryHandlers handler, CancellationToken ct) =>
        {
            var filter = new CollectionQuery(query, game, setId, condition, variantId, page ?? 1, pageSize ?? 20, sort ?? "name");
            return Results.Ok(await handler.Handle(new GetMyCollectionQuery(UserId(principal), filter), ct));
        }).WithName("GetMyCollection").Produces<CollectionPageDto>().ProducesProblem(400).ProducesProblem(401);

        collection.MapGet("/summary", async (ClaimsPrincipal principal, CollectionQueryHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new GetCollectionSummaryQuery(UserId(principal)), ct)))
            .WithName("GetMyCollectionSummary").Produces<CollectionSummaryDto>().ProducesProblem(401);

        collection.MapPost("/items", async (AddCollectibleItemsRequest request, HttpRequest httpRequest, ClaimsPrincipal principal, CollectionCommandHandlers handler, CancellationToken ct) =>
        {
            var idempotencyKey = httpRequest.Headers.TryGetValue("Idempotency-Key", out var values) ? values.ToString() : null;
            var result = await handler.Handle(new AddCollectibleItemsCommand(UserId(principal), request, idempotencyKey), ct);
            return Results.Created($"/api/v1/me/collection/entries/{result.CollectionEntryId}", result);
        }).WithName("AddCollectibleItems").Produces<AddCollectibleItemsResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        collection.MapGet("/entries/{entryId:guid}", async (Guid entryId, int? page, int? pageSize, ClaimsPrincipal principal, CollectionQueryHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new GetCollectionEntryQuery(UserId(principal), entryId, page ?? 1, pageSize ?? 20), ct)))
            .WithName("GetCollectionEntry").Produces<CollectionEntryDetailsDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        collection.MapGet("/items/{itemId:guid}", async (Guid itemId, ClaimsPrincipal principal, CollectionQueryHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new GetCollectibleItemQuery(UserId(principal), itemId), ct)))
            .WithName("GetCollectibleItem").Produces<CollectibleItemDto>().ProducesProblem(401).ProducesProblem(404);

        collection.MapPut("/items/{itemId:guid}", async (Guid itemId, UpdateCollectibleItemRequest request, ClaimsPrincipal principal, CollectionCommandHandlers handler, CancellationToken ct) =>
        {
            var version = await handler.Handle(new UpdateCollectibleItemCommand(UserId(principal), itemId, request), ct);
            return Results.Ok(new { version });
        }).WithName("UpdateCollectibleItem").Produces(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        collection.MapDelete("/items/{itemId:guid}", async (Guid itemId, Guid version, ClaimsPrincipal principal, CollectionCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new RemoveCollectibleItemCommand(UserId(principal), itemId, version), ct);
            return Results.NoContent();
        }).WithName("RemoveCollectibleItem").Produces(204).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        collection.MapPost("/items/{itemId:guid}/assets", async (Guid itemId, AttachCollectibleItemAssetRequest request, ClaimsPrincipal principal, CollectionCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new AttachCollectibleItemAssetCommand(UserId(principal), itemId, request), ct);
            return Results.NoContent();
        }).WithName("AttachCollectibleItemAsset").Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        collection.MapDelete("/items/{itemId:guid}/assets/{assetId:guid}", async (Guid itemId, Guid assetId, ClaimsPrincipal principal, CollectionCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new RemoveCollectibleItemAssetCommand(UserId(principal), itemId, assetId), ct);
            return Results.NoContent();
        }).WithName("RemoveCollectibleItemAsset").Produces(204).ProducesProblem(401).ProducesProblem(404);

        collection.MapPut("/items/{itemId:guid}/assets/{assetId:guid}/primary", async (Guid itemId, Guid assetId, ClaimsPrincipal principal, CollectionCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new SetPrimaryCollectibleItemAssetCommand(UserId(principal), itemId, assetId), ct);
            return Results.NoContent();
        }).WithName("SetPrimaryCollectibleItemAsset").Produces(204).ProducesProblem(401).ProducesProblem(404);
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);
}
