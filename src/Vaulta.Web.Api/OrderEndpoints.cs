using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vaulta.Orders.Application;
using Vaulta.Orders.Application.Commands;
using Vaulta.Orders.Application.Queries;
using Vaulta.Orders.Contracts;

namespace Vaulta.Web.Api;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var orders = app.MapGroup("/api/v1/orders").WithTags("Orders").RequireAuthorization();

        orders.MapPost("", async (CreateOrderRequest request, ClaimsPrincipal principal,
            OrderCommandHandlers handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new CreateOrderCommand(UserId(principal), request), ct);
            return Results.Created($"/api/v1/orders/{result.Id}", result);
        }).WithName("CreateOrder").Produces<OrderDto>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        orders.MapGet("/{orderId:guid}", async (Guid orderId, ClaimsPrincipal principal,
            IOrderQueries queries, CancellationToken ct) =>
        {
            var order = await queries.GetById(UserId(principal), orderId, ct);
            return order is null ? Results.NotFound() : Results.Ok(order);
        }).WithName("GetOrderById").Produces<OrderDto>().ProducesProblem(401).ProducesProblem(404);

        orders.MapGet("", async (string? status, int? page, int? pageSize, ClaimsPrincipal principal,
            IOrderQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetUserOrders(UserId(principal), status, page ?? 1, pageSize ?? 20, ct)))
            .WithName("GetUserOrders").Produces<OrderPageDto>().ProducesProblem(401);

        orders.MapPost("/{orderId:guid}/confirm-payment", async (Guid orderId, ConfirmPaymentRequest request,
            OrderCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new ConfirmPaymentCommand(orderId, request.PaymentId), ct);
            return Results.NoContent();
        }).WithName("ConfirmPayment").Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        orders.MapPost("/{orderId:guid}/ship", async (Guid orderId, MarkShippedRequest request,
            ClaimsPrincipal principal, OrderCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new MarkShippedCommand(UserId(principal), orderId, request.TrackingCode), ct);
            return Results.NoContent();
        }).WithName("MarkShipped").Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        orders.MapPost("/{orderId:guid}/deliver", async (Guid orderId, OrderCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new MarkDeliveredCommand(orderId), ct);
            return Results.NoContent();
        }).WithName("MarkDelivered").Produces(204).ProducesProblem(401).ProducesProblem(404);

        orders.MapPost("/{orderId:guid}/cancel", async (Guid orderId, CancelOrderRequest request,
            ClaimsPrincipal principal, OrderCommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new CancelOrderCommand(UserId(principal), orderId, request.Reason), ct);
            return Results.NoContent();
        }).WithName("CancelOrder").Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);
}