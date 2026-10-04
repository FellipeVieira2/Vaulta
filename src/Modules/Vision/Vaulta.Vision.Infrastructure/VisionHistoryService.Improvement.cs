using Microsoft.EntityFrameworkCore;
using Vaulta.SharedKernel;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Infrastructure;

public sealed partial class VisionHistoryService
{
    private async Task<VisionImprovementFeedbackDto?> TryDeveloperPromotionAsync(Guid owner,Guid attempt,VisionFeedback feedback,CancellationToken ct)
    {
        if(improvement?.Allows(owner)!=true)return null;
        if(feedback.Source is not ("UserConfirmation" or "UserCorrection") || feedback.PrintingId is null
            || feedback.Orientation!="front" || feedback.Presence!="card-present")return new("not_eligible",null,refresh?.Pending??false,null,refresh?.LastRefresh);
        var run=await db.ScanRuns.AsNoTracking().SingleAsync(x=>x.Id==feedback.RunId,ct);
        if(run.Status!="completed" || run.Embedding is null || run.ManifestJson is null)return new("not_eligible",null,false,null,refresh?.LastRefresh);
        try
        {
            var sample=await PromoteAsync(attempt,feedback.Id,ct,"developer-auto");
            return new("promoted",sample,refresh?.Pending??true,null,refresh?.LastRefresh);
        }
        catch(Exception error) when(error is not OperationCanceledException)
        {
            // Feedback was committed separately. Rolled-back promotion additions
            // must not escape into a later SaveChanges in the same scope.
            foreach(var entry in db.ChangeTracker.Entries<VisionReviewedSample>().Where(x=>x.State==EntityState.Added).ToArray())entry.State=EntityState.Detached;
            foreach(var entry in db.ChangeTracker.Entries<VisualReference>().Where(x=>x.State==EntityState.Added).ToArray())entry.State=EntityState.Detached;
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger??Microsoft.Extensions.Logging.Abstractions.NullLogger<VisionHistoryService>.Instance,"Developer promotion deferred: {ErrorType}",error.GetType().Name);
            return new("awaiting_verification",null,refresh?.Pending??false,null,refresh?.LastRefresh);
        }
    }
    public bool IsDeveloper(Guid owner)=>improvement?.Allows(owner)==true;
    public async Task<object> ImprovementStatusAsync(Vaulta.Vision.Application.IVisualReferenceIndex? index,CancellationToken ct)
    {
        var origins=await db.VisualReferences.AsNoTracking().Where(x=>x.Status=="ready").GroupBy(x=>x.Origin).Select(g=>new{Origin=g.Key,Count=g.Count()}).ToArrayAsync(ct);
        var active=db.ScanAttempts.Where(a=>a.DeletionRequestedAt==null && a.RetentionUntil>clock.UtcNow);
        var feedback=from f in db.Feedback join a in active on f.AttemptId equals a.Id where !db.Feedback.Any(n=>n.RunId==f.RunId && n.CreatedAt>f.CreatedAt) select f;
        return new
        {
            encoder=index?.Status.Identity,modelVersion=index is null?null:VisualReferenceBuilder.ModelVersion(index.Status.Identity),
            officialReferences=origins.Where(x=>x.Origin=="official").Sum(x=>x.Count),
            verifiedCaptureReferences=origins.Where(x=>x.Origin=="verified_capture").Sum(x=>x.Count),
            pendingFeedback=await feedback.CountAsync(f=>!db.ReviewedSamples.Any(s=>s.FeedbackId==f.Id),ct),
            reviewedSamples=await db.ReviewedSamples.CountAsync(s=>active.Any(a=>a.Id==s.AttemptId),ct),
            realWorldCaptures=await db.ScanCaptures.CountAsync(c=>c.Status=="ready" && active.Any(a=>a.Id==c.AttemptId),ct),
            indexReferences=index?.Status.ReferenceCount,indexVersion=index?.Status.Version,
            historyEnabled=options.Enabled,improvementPolicyEnabled=options.Enabled && options.ImprovementPolicyVersion is not null,
            developerAutoPromotion=improvement?.DeveloperAutoPromote??false,
            indexRefreshPending=refresh?.Pending??false,lastIndexRefresh=refresh?.LastRefresh
        };
    }
    private async Task RetryDeveloperFeedbackAsync(Guid owner,Guid attempt,CancellationToken ct)
    {
        if(improvement?.Allows(owner)!=true)return;
        var feedback=await db.Feedback.AsNoTracking().Where(x=>x.AttemptId==attempt)
            .GroupBy(x=>x.RunId).Select(g=>g.OrderByDescending(x=>x.CreatedAt).First()).ToArrayAsync(ct);
        foreach(var f in feedback)await TryDeveloperPromotionAsync(owner,attempt,f,ct);
    }
}
