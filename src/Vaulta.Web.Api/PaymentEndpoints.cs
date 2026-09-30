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

        payments.MapPost("", async (CreatePaymentRequest request, ClaimsPrincipal principal,
            PaymentCommandHandlers handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new InitiatePaymentCommand(UserId(principal), request), ct);
            return Results.Created($"/api/v1/payments/{result.Id}", result);
        }).WithName("InitiatePayment").Produces<PaymentDto>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        // Webhook endpoint - no auth required, validated by Asaas signature/token
        app.MapPost("/api/v1/webhooks/asaas", async (HttpRequest httpRequest, PaymentCommandHandlers handler, CancellationToken ct) =>
        {
            using var reader = new StreamReader(httpRequest.Body);
            var payload = await reader.ReadToEndAsync(ct);
            string? eventType = httpRequest.Headers["X-Asaas-Event"].FirstOrDefault();
            string? paymentId = null;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("payment", out var payment))
                    paymentId = payment.GetProperty("id").GetString();
                else if (doc.RootElement.TryGetProperty("id", out var id))
                    paymentId = id.GetString();
            }
            catch { }

            if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(paymentId))
                return Results.BadRequest(new { error = "Missing event type or payment ID." });

            await handler.Handle(new ProcessWebhookCommand(eventType, paymentId, payload), ct);
            return Results.Ok(new { received = true });
        }).WithName("AsaasWebhook").Produces(200).ProducesProblem(400);
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);
}