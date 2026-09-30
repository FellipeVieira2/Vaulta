using Microsoft.AspNetCore.Mvc;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Web.Api;

public static class ScannerEndpoints
{
    public static void MapScannerEndpoints(this WebApplication app)
    {
        var scanner = app.MapGroup("/api/v1/scanner").WithTags("Scanner").RequireAuthorization();

        scanner.MapPost("/identify", async (IFormFile image, string? gameCode, ScannerService service, CancellationToken ct) =>
        {
            if (image is null || image.Length == 0)
                return Results.BadRequest(new { error = "Image file is required." });

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