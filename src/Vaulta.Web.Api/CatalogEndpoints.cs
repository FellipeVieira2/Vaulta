using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Domain;

namespace Vaulta.Web.Api;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var catalog = app.MapGroup("/api/v1/catalog").WithTags("Catalog");
        catalog.MapGet("/printings/{id:guid}/artwork", async (Guid id, bool? thumbnail, Vaulta.Catalog.Infrastructure.CatalogDbContext db, Vaulta.Assets.Application.ISystemAssetService assets, CancellationToken ct) =>
        {
            var asset=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(
                db.Printings.Where(x=>x.Id==id && x.IsActive).Select(x=>thumbnail==true ? x.ThumbnailAssetId : x.ArtworkAssetId),ct);
            var url=asset is null ? null : await assets.GetArtworkReadUrlAsync(asset.Value,ct);
            return url is null ? Results.NotFound() : Results.Redirect(url);
        });
        catalog.MapGet("/search", async (string q, string? game, int? page, int? pageSize, ICatalogSearch search, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length > 200 || CatalogNormalizer.NormalizeName(q).Length == 0)
                return Results.Problem(statusCode: 400, title: "Query must contain searchable characters and be no longer than 200 characters.");
            var currentPage = page ?? 1;
            var limit = pageSize ?? 20;
            if (currentPage < 1 || limit is < 1 or > 100 || (long)(currentPage - 1) * limit > int.MaxValue) return Results.Problem(statusCode: 400, title: "Invalid pagination values.");
            return Results.Ok(await search.Search(q, game, currentPage, limit, ct));
        }).WithName("SearchCatalog").Produces<CatalogSearchPage>().ProducesProblem(400);

        catalog.MapGet("/printings/{id:guid}", async (Guid id, ICatalogSearch search, CancellationToken ct) =>
        {
            var result = await search.GetPrinting(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("GetCatalogPrinting").Produces<CatalogPrintingDetails>().ProducesProblem(404);
    }
}
