using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vaulta.Shipping.Application;
using Vaulta.Shipping.Application.Commands;
using Vaulta.Shipping.Contracts;

namespace Vaulta.Web.Api;

public static class ShippingEndpoints
{
    public static void MapShippingEndpoints(this WebApplication app)
    {
        var shipping = app.MapGroup("/api/v1/shipping").WithTags("Shipping").RequireAuthorization();

        shipping.MapPost("", async (CreateShipmentRequest request, ClaimsPrincipal principal,
            ShippingCommandHandlers handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new CreateShipmentCommand(UserId(principal), request), ct);
            return Results.Created($"/api/v1/shipping/{result.Id}", result);
        }).WithName("CreateShipment").Produces<ShipmentDto>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        shipping.MapGet("/{shipmentId:guid}", async (Guid shipmentId, ClaimsPrincipal principal,
            IShippingStore store, CancellationToken ct) =>
        {
            var shipment = await store.FindById(shipmentId, ct);
            if (shipment is null) return Results.NotFound();
            if (shipment.SellerId != UserId(principal) && shipment.BuyerId != UserId(principal))
                return Results.Forbid();
            return Results.Ok(ShippingCommandHandlers.MapShipment(shipment));
        }).WithName("GetShipment").Produces<ShipmentDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        shipping.MapGet("/order/{orderId:guid}", async (Guid orderId, ClaimsPrincipal principal,
            IShippingStore store, CancellationToken ct) =>
        {
            var shipment = await store.FindByOrderId(orderId, ct);
            if (shipment is null) return Results.NotFound();
            if (shipment.SellerId != UserId(principal) && shipment.BuyerId != UserId(principal))
                return Results.Forbid();
            return Results.Ok(ShippingCommandHandlers.MapShipment(shipment));
        }).WithName("GetShipmentByOrder").Produces<ShipmentDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        shipping.MapPut("/{shipmentId:guid}/tracking", async (Guid shipmentId, UpdateTrackingRequest request,
            ClaimsPrincipal principal, ShippingCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new UpdateTrackingCommand(UserId(principal), shipmentId, request), ct);
            return Results.NoContent();
        }).WithName("UpdateTracking").Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);
}