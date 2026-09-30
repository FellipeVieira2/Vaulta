using Microsoft.AspNetCore.Mvc;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Web.Api;

public static class ScannerEndpoints
{
    public static void MapScannerEndpoints(this WebApplication app)
    {
        var scanner = app.MapGroup("/api/v1/scanner").WithTags("Scanner").RequireAuthorization();

        scanner.MapPost("/identify", async (CardScanRequest request, ScannerService service, CancellationToken ct) =>
        {
            var result = await service.IdentifyAsync(request, ct);
            return Results.Ok(result);
        }).WithName("IdentifyCard").Produces<CardScanResultDto>().ProducesProblem(400).ProducesProblem(401);

        scanner.MapGet("/search", async (string query, string? gameCode, ScannerService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(query))
                return Results.BadRequest(new { error = "Query parameter is required." });
            var result = await service.SearchByNameAsync(query, gameCode, ct);
            return Results.Ok(result);
        }).WithName("SearchCards").Produces<CardScanResultDto>().ProducesProblem(400).ProducesProblem(401);
    }
}