using Microsoft.AspNetCore.Mvc;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;

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

        scanner.MapPost("/identify", async (IFormFile image, [FromQuery] string? gameCode, VisionScannerService service, CancellationToken ct) =>
        {
            if (image is null || image.Length == 0)
                return Results.Problem(statusCode: 400, title: "Envie uma imagem da carta.");
            if (image.Length > 15 * 1024 * 1024)
                return Results.Problem(statusCode: 413, title: "A imagem deve ter atÃ© 15 MB.");
            if (image.ContentType is not ("image/jpeg" or "image/png" or "image/webp"))
                return Results.Problem(statusCode: 415, title: "Use uma imagem JPEG, PNG ou WebP.");

            using var stream = image.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, ct);
            var imageData = memoryStream.ToArray();

            var request = new VisionScanInput([new(imageData)], gameCode);
            try { return Results.Ok(await service.IdentifyAsync(request, ct)); }
            catch(Exception error) when(error is InvalidDataException or SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException)
            { return Results.Problem(statusCode:400,title:"Não foi possível processar essa imagem. Envie uma foto válida da carta."); }
        }).WithName("IdentifyCard").DisableAntiforgery().Produces<VisionScanResultDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(429);

        scanner.MapGet("/search", async (string query, string? gameCode, ICatalogSearch catalog, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(query))
                return Results.BadRequest(new { error = "Query parameter is required." });
            var found=await catalog.Search(query,gameCode,1,20,ct);
            var result=new CardScanResultDto(found.Items.Select(x=>new CardScanCandidateDto(x.PrintingId,x.CardName,x.SetName,x.CollectorNumber,x.Rarity,x.ArtworkUrl,null,null,[],0)).ToArray());
            return Results.Ok(result);
        }).WithName("SearchCards").Produces<CardScanResultDto>().ProducesProblem(400).ProducesProblem(401);
    }
}
