using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vaulta.Assets.Application;
using Vaulta.Assets.Contracts;

namespace Vaulta.Web.Api;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var assets = app.MapGroup("/api/v1/assets").WithTags("Assets").RequireAuthorization();
        assets.MapPost("/uploads", async (CreateAssetUploadRequest request, ClaimsPrincipal principal, IAssetService service, CancellationToken ct) =>
        {
            if(request.Purpose=="vision-scan") return Results.Problem(statusCode:400,title:"Use the consent-validated Vision capture endpoint.");
            var ownerId = Guid.Parse(principal.FindFirstValue("sub")!);
            return Results.Ok(await service.CreateUpload(ownerId, request, ct));
        }).WithName("CreateAssetUpload").Produces<CreateAssetUploadResponse>().ProducesProblem(400).ProducesProblem(401);
        assets.MapPost("/{assetId:guid}/confirm", async (Guid assetId, ClaimsPrincipal principal, IAssetService service, CancellationToken ct) =>
        {
            var ownerId = Guid.Parse(principal.FindFirstValue("sub")!);
            var response = await service.ConfirmUpload(ownerId, assetId, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        }).WithName("ConfirmAssetUpload").Produces<ConfirmAssetUploadResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);
    }
}
