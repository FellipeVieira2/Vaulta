using System.Security.Claims;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Infrastructure;
using Vaulta.Assets.Application;
namespace Vaulta.Web.Api;
public static class VisionEndpoints
{
 private static Guid Owner(ClaimsPrincipal p)=>Guid.Parse(p.FindFirstValue("sub")!);
 public static void MapVisionEndpoints(this WebApplication app)
 {
  var v=app.MapGroup("/api/v1/vision").RequireAuthorization().WithTags("Vision");
  v.MapGet("/policies",(VisionHistoryService s)=>Results.Ok(s.Policies));
  v.MapPost("/attempts",async(CreateScanAttemptRequest r,ClaimsPrincipal p,VisionHistoryService s,CancellationToken ct)=>Results.Ok(await s.CreateAsync(Owner(p),r,ct)));
  v.MapGet("/attempts/{id:guid}",async(Guid id,ClaimsPrincipal p,VisionHistoryService s,CancellationToken ct)=>Results.Ok(await s.GetAsync(Owner(p),id,ct)));
  v.MapDelete("/attempts/{id:guid}",async(Guid id,ClaimsPrincipal p,VisionHistoryService s,CancellationToken ct)=>{await s.DeleteAsync(Owner(p),id,ct);return Results.NoContent();});
  v.MapPost("/attempts/{id:guid}/captures/uploads",async(Guid id,CreateVisionCaptureRequest r,ClaimsPrincipal p,VisionHistoryService s,IAssetService a,CancellationToken ct)=>Results.Ok(await s.UploadAsync(Owner(p),id,r,a,ct)));
  v.MapPost("/attempts/{id:guid}/captures/{capture:guid}/confirm",async(Guid id,Guid capture,ClaimsPrincipal p,VisionHistoryService s,IAssetService a,CancellationToken ct)=>Results.Ok(await s.ConfirmAsync(Owner(p),id,capture,a,ct)));
  v.MapGet("/attempts/{id:guid}/captures/{capture:guid}/read",async(Guid id,Guid capture,ClaimsPrincipal p,VisionHistoryService s,IAssetService a,CancellationToken ct)=>
  {var owner=Owner(p);var attempt=await s.GetAsync(owner,id,ct);if(attempt.Status!="active" || attempt.RetentionUntil<=DateTimeOffset.UtcNow)return Results.NotFound();var c=attempt.Captures.SingleOrDefault(x=>x.Id==capture && x.Status=="ready");if(c is null)return Results.NotFound();var url=await a.CreatePrivateReadUrl(owner,c.AssetId,ct);return url is null?Results.NotFound():Results.Ok(url);});
  v.MapPost("/attempts/{id:guid}/feedback",async(Guid id,VisionFeedbackRequest r,HttpRequest http,ClaimsPrincipal p,VisionHistoryService s,CancellationToken ct)=>Results.Ok(await s.FeedbackAsync(Owner(p),id,http.Headers["X-Feedback-ID"].FirstOrDefault()??"",r,ct)));
 }
}
