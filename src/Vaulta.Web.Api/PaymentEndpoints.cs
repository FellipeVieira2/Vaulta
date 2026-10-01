using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vaulta.Payments.Application;
using Vaulta.Payments.Application.Commands;
using Vaulta.Payments.Contracts;

namespace Vaulta.Web.Api;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        var payments = app.MapGroup("/api/v1/payments").WithTags("Payments").RequireAuthorization();
        payments.MapGet("/customer", async (ClaimsPrincipal principal, BuyerCustomerService service, CancellationToken ct) =>
        {
            var result = await service.Get(UserId(principal), ct);
            return result is null ? Results.NoContent() : Results.Ok(result);
        });
        payments.MapPut("/customer", async (RegisterBuyerCustomerRequest request, ClaimsPrincipal principal,
            BuyerCustomerService service, CancellationToken ct) => Results.Ok(await service.Register(UserId(principal), request, ct)));
        payments.MapGet("/orders/{orderId:guid}", async (Guid orderId, ClaimsPrincipal principal, IPaymentStore store,
            Vaulta.Orders.Application.IOrderStore orders, CancellationToken ct) =>
        {
            var order = await orders.FindOrderForUser(UserId(principal), orderId, ct);
            if (order is null) return Results.NotFound();
            if (order.BuyerId != UserId(principal)) return Results.Forbid();
            var payment = await store.FindByOrderId(orderId, ct);
            return payment is null ? Results.NoContent() : Results.Ok(PaymentCommandHandlers.MapPaymentForBuyer(payment));
        });

        payments.MapPost("", async (CreatePaymentRequest request, ClaimsPrincipal principal,
            PaymentCommandHandlers handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new InitiatePaymentCommand(UserId(principal), request), ct);
            return Results.Created($"/api/v1/payments/{result.Id}", result);
        }).WithName("InitiatePayment").Produces<PaymentDto>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        var payouts = app.MapGroup("/api/v1/payouts").WithTags("Seller payouts").RequireAuthorization();
        payouts.MapGet("", async (ClaimsPrincipal principal, IPayoutStore store, CancellationToken ct, int page = 1, int pageSize = 20) =>
            Results.Ok(await store.ListMine(UserId(principal), page, pageSize, ct)));
        payouts.MapGet("/pix", async (ClaimsPrincipal principal, SellerPayoutService service, CancellationToken ct) =>
        {
            var destination = await service.GetDestination(UserId(principal), ct);
            return destination is null ? Results.NoContent() : Results.Ok(destination);
        });
        payouts.MapPut("/pix", async (RegisterPixDestinationRequest request, ClaimsPrincipal principal, SellerPayoutService service, CancellationToken ct) =>
            Results.Ok(await service.RegisterDestination(UserId(principal), request, ct)));
        // Authorization is a server-controlled list of trusted identity reviewers;
        // the existing 'admin' seed account has no implicit financial privileges.
        payouts.MapGet("/pix/reviews/{sellerId:guid}", async (Guid sellerId, ClaimsPrincipal principal, SellerPayoutService service, CancellationToken ct) =>
            Results.Ok(await service.GetForReview(UserId(principal), sellerId, ct)));
        payouts.MapPost("/pix/reviews/{sellerId:guid}/verify", async (Guid sellerId, VerifyPixDestinationRequest request,
            ClaimsPrincipal principal, SellerPayoutService service, CancellationToken ct) =>
            Results.Ok(await service.VerifyDestination(UserId(principal), sellerId, request, ct)));

        // Anonymous endpoint: authenticated by the shared token Asaas sends in the asaas-access-token header.
        app.MapPost("/api/v1/webhooks/asaas", async (HttpRequest httpRequest, IConfiguration configuration,
            PaymentCommandHandlers handler, SellerPayoutService payoutService, CancellationToken ct) =>
        {
            var expectedToken = configuration["Asaas:WebhookToken"];
            if (string.IsNullOrWhiteSpace(expectedToken))
                return Results.Problem(statusCode: 503, title: "Webhook is not configured.");

            if (!IsValidToken(httpRequest.Headers["asaas-access-token"].FirstOrDefault(), expectedToken))
                return Results.Unauthorized();

            using var reader = new StreamReader(httpRequest.Body);
            var payload = await reader.ReadToEndAsync(ct);
            string? eventType = null;
            string? paymentId = null;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return Results.BadRequest(new { error = "Invalid webhook object." });
                eventType = String(root, "event");
                if (eventType?.StartsWith("TRANSFER_", StringComparison.Ordinal) == true)
                {
                    var eventId = String(root, "id");
                    if (!root.TryGetProperty("transfer", out var transfer) || transfer.ValueKind != System.Text.Json.JsonValueKind.Object
                        || !transfer.TryGetProperty("value", out var value) || value.ValueKind != System.Text.Json.JsonValueKind.Number
                        || !value.TryGetDecimal(out var amount))
                        return Results.BadRequest(new { error = "Missing transfer information." });
                    var transferId = String(transfer, "id");
                    var reference = String(transfer, "externalReference");
                    var status = String(transfer, "status");
                    // The account can receive events for unrelated provider
                    // transfers. Acknowledge them without touching Vaulta records.
                    if (reference is null || !reference.StartsWith("vaulta_payout_", StringComparison.Ordinal))
                        return Results.Ok(new { received = true, ignored = true });
                    if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(transferId)
                        || string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(status))
                        return Results.BadRequest(new { error = "Missing transfer identifiers." });
                    if (!transfer.TryGetProperty("netValue", out var netValue) || netValue.ValueKind != System.Text.Json.JsonValueKind.Number || !netValue.TryGetDecimal(out var netAmount)
                        || !transfer.TryGetProperty("transferFee", out var feeValue) || feeValue.ValueKind != System.Text.Json.JsonValueKind.Number || !feeValue.TryGetDecimal(out var feeAmount))
                        return Results.BadRequest(new { error = "Missing transfer net value or fee." });
                    await payoutService.ProcessWebhook(eventId, eventType, new(transferId, status, amount, reference, netAmount, feeAmount), ct);
                    return Results.Ok(new { received = true });
                }
                if (root.TryGetProperty("payment", out var payment) && payment.ValueKind == System.Text.Json.JsonValueKind.Object)
                    paymentId = String(payment, "id");
            }
            catch (System.Text.Json.JsonException) { }

            if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(paymentId))
                return Results.BadRequest(new { error = "Missing event type or payment ID." });

            await handler.Handle(new ProcessWebhookCommand(eventType, paymentId, payload), ct);
            return Results.Ok(new { received = true });
        }).WithName("AsaasWebhook").Produces(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(503);
    }

    private static string? String(System.Text.Json.JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;

    private static bool IsValidToken(string? provided, string expected)
    {
        if (provided is null) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(provided);
        var b = System.Text.Encoding.UTF8.GetBytes(expected);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);
}
