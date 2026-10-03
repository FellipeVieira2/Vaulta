using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Vaulta.SharedKernel;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Infrastructure;
using Xunit;
namespace Vaulta.Identity.IntegrationTests;
[Collection("api")]
public class VisionHistoryTests(ApiFixture fixture)
{
    private const string Sha="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    [Fact] public async Task AnotherOwnerCannotReadOrReserveAttempt()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var service=Service(scope);var owner=Guid.NewGuid();var a=await service.CreateAsync(owner,new("ops-v1"),default);
        await Assert.ThrowsAsync<NotFoundException>(()=>service.GetAsync(Guid.NewGuid(),a.AttemptId,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.ReserveAsync(Guid.NewGuid(),a.AttemptId,"exec",Sha,default));
    }
    [Fact] public async Task SameExecutionKeyCannotUseAnotherImageOrDuplicateInFlight()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var service=Service(scope);var owner=Guid.NewGuid();var a=await service.CreateAsync(owner,new("ops-v1"),default);
        await service.ReserveAsync(owner,a.AttemptId,"exec",Sha,default);
        await Assert.ThrowsAsync<ConflictException>(()=>service.ReserveAsync(owner,a.AttemptId,"exec",new string('b',64),default));
        await Assert.ThrowsAsync<ConflictException>(()=>service.ReserveAsync(owner,a.AttemptId,"exec",Sha,default));
    }
    [Fact] public async Task SeparatePhysicalAttemptsMayHaveIdenticalBytes()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var service=Service(scope);var owner=Guid.NewGuid();var a=await service.CreateAsync(owner,new("ops-v1"),default);var b=await service.CreateAsync(owner,new("ops-v1"),default);
        var r1=await service.ReserveAsync(owner,a.AttemptId,"exec",Sha,default);var r2=await service.ReserveAsync(owner,b.AttemptId,"exec",Sha,default);Assert.NotEqual(r1.Id,r2.Id);
    }
    [Fact] public async Task PolicyMismatchAndExpiredAttemptsAreRejected()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var service=Service(scope);var owner=Guid.NewGuid();
        await Assert.ThrowsAsync<DomainException>(()=>service.CreateAsync(owner,new("old-policy"),default));
        var a=await service.CreateAsync(owner,new("ops-v1"),default);var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();var row=await db.ScanAttempts.SingleAsync(x=>x.Id==a.AttemptId);row.RetentionUntil=DateTimeOffset.UtcNow.AddDays(-1);await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(()=>service.ReserveAsync(owner,a.AttemptId,"exec",Sha,default));
    }
    [Fact] public async Task CompletedRunReplaysSameScanIdWithoutPricesOrOverwritingPrediction()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var service=Service(scope);var owner=Guid.NewGuid();var a=await service.CreateAsync(owner,new("ops-v1"),default);
        var run=await service.ReserveAsync(owner,a.AttemptId,"exec",Sha,default);var prediction=Prediction();
        await service.CompleteAsync(run.Id,prediction,new(prediction.Trace.Encoder,[1,0,0]),default);
        var replay=await service.ReserveAsync(owner,a.AttemptId,"exec",Sha,default);Assert.Equal(prediction.ScanId,VisionHistoryService.Replay(replay).ScanId);
        var details=await service.GetAsync(owner,a.AttemptId,default);Assert.Single(details.Runs);Assert.True(details.Runs[0].HasEmbedding);Assert.Null(details.Runs[0].Prediction!.Price);
        var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();Assert.DoesNotContain("price-value",(await db.ScanRuns.SingleAsync(x=>x.Id==run.Id)).PredictionJson!);
        await Assert.ThrowsAsync<ConflictException>(()=>service.CompleteAsync(run.Id,prediction,new(prediction.Trace.Encoder,[1,0,0]),default));
    }
    [Fact] public async Task PublicFeedbackCannotEscalateOrAttachAnotherPrintingsVariant()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var service=Service(scope);var owner=Guid.NewGuid();var a=await service.CreateAsync(owner,new("ops-v1"),default);var run=await service.ReserveAsync(owner,a.AttemptId,"exec",Sha,default);var prediction=Prediction();await service.CompleteAsync(run.Id,prediction,new(prediction.Trace.Encoder,[1,0,0]),default);
        await Assert.ThrowsAsync<DomainException>(()=>service.FeedbackAsync(owner,a.AttemptId,"f1",new(run.Id,"AdminReview",Orientation:"front"),default));
        await Assert.ThrowsAsync<DomainException>(()=>service.FeedbackAsync(owner,a.AttemptId,"f2",new(run.Id,"UserCorrection",Guid.NewGuid(),Guid.NewGuid(),"front","card-present"),default));
    }
    [Fact] public async Task OrientationFeedbackIsImmutableAndDoesNotAutomaticallyPromote()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var service=Service(scope);var owner=Guid.NewGuid();var a=await service.CreateAsync(owner,new("ops-v1"),default);var run=await service.ReserveAsync(owner,a.AttemptId,"exec",Sha,default);var prediction=Prediction();await service.CompleteAsync(run.Id,prediction,new(prediction.Trace.Encoder,[1,0,0]),default);
        var feedback=await service.FeedbackAsync(owner,a.AttemptId,"f1",new(run.Id,"UserCorrection",Orientation:"back",Presence:"card-present"),default);
        var replay=await service.FeedbackAsync(owner,a.AttemptId,"f1",feedback.Feedback,default);Assert.Equal(feedback.Id,replay.Id);
        await Assert.ThrowsAsync<ConflictException>(()=>service.FeedbackAsync(owner,a.AttemptId,"f1",feedback.Feedback with {Orientation="front"},default));
        await Assert.ThrowsAsync<DomainException>(()=>service.PromoteAsync(a.AttemptId,feedback.Id,default));
        var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();Assert.False(await db.ReviewedSamples.AnyAsync(x=>x.AttemptId==a.AttemptId));Assert.Equal("not_a_card",VisionHistoryService.Replay(await db.ScanRuns.SingleAsync(x=>x.Id==run.Id)).Status);
    }
    private static VisionScanResultDto Prediction()=>new(Guid.NewGuid(),"not_a_card",null,null,.9,0,[],null,"unresolved",null,new Dictionary<string,VisionFieldDto>(),new(new("fixture","fixture","fixture","fixture",3,"test","input","output"),"fixture-index","fixture-resolver",null,null,2));
    private static VisionHistoryService Service(AsyncServiceScope scope)=>new(scope.ServiceProvider.GetRequiredService<VisionDbContext>(),scope.ServiceProvider.GetRequiredService<Vaulta.Assets.Application.IPrivateAssetService>(),scope.ServiceProvider.GetRequiredService<Vaulta.Vision.Application.IVisionCatalog>(),scope.ServiceProvider.GetRequiredService<IClock>(),new VisionHistoryOptions {Enabled=true,OperationalPolicyVersion="ops-v1",ImprovementPolicyVersion="improve-v1",RetentionDays=7});
}
