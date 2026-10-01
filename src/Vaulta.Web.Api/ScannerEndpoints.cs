using Microsoft.AspNetCore.Mvc;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Web.Api;

public static class ScannerEndpoints
{
    public static void MapScannerEndpoints(this WebApplication app)
    {
        var scanner = app.MapGroup("/api/v1/scanner").WithTags("Scanner").RequireAuthorization();

        scanner.MapGet("/printings/{printingId:guid}", async (Guid printingId, IScannerCardDetailsReader reader, CancellationToken ct) =>
        {
            var result = await reader.GetAsync(printingId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("GetScannerCardDetails").Produces<ScannerCardDetailsDto>().ProducesProblem(401).Produces(404);

        scanner.MapPost("/identify", async (IFormFile image, [FromQuery] string? gameCode, ScannerService service, CancellationToken ct) =>
        {
            if (image is null || image.Length == 0)
                return Results.Problem(statusCode: 400, title: "Envie uma imagem da carta.");
            if (image.Length > 15 * 1024 * 1024)
                return Results.Problem(statusCode: 413, title: "A imagem deve ter até 15 MB.");
            if (image.ContentType is not ("image/jpeg" or "image/png" or "image/webp"))
                return Results.Problem(statusCode: 415, title: "Use uma imagem JPEG, PNG ou WebP.");

            using var stream = image.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, ct);
            var imageData = memoryStream.ToArray();

            var request = new CardScanRequest(Convert.ToBase64String(imageData), gameCode);
            var result = await service.IdentifyAsync(request, ct);
            return Results.Ok(result);
        }).WithName("IdentifyCard").DisableAntiforgery().Produces<CardScanResultDto>().ProducesProblem(400).ProducesProblem(401);

        scanner.MapGet("/search", async (string query, string? gameCode, ScannerService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(query))
                return Results.BadRequest(new { error = "Query parameter is required." });
            var result = await service.SearchByNameAsync(query, gameCode, ct);
            return Results.Ok(result);
        }).WithName("SearchCards").Produces<CardScanResultDto>().ProducesProblem(400).ProducesProblem(401);
    }
}
