using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Assets.Application;
using Vaulta.Catalog.Domain;
using Vaulta.Catalog.Infrastructure;
using Vaulta.SharedKernel;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Infrastructure;
using Xunit;
namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public class VisionDeveloperImprovementTests(ApiFixture fixture)
{
    [Theory]
    [InlineData(true,true,true,true)]
    [InlineData(false,true,true,false)]
    [InlineData(true,false,true,false)]
    [InlineData(true,true,false,false)]
    public async Task OnlyConsentingAllowlistedDeveloperFeedbackBecomesReference(bool enabled,bool allowed,bool consent,bool promoted)
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var h=await Harness.Create(scope,enabled,allowed);
        var (attempt,run,capture)=await h.Attempt(consent,true);
        Assert.False(await h.Db.VisualReferences.AnyAsync(x=>x.SourceAssetId==capture.AssetId)); // Prediction alone is never ground truth.
        var f=await h.Service.FeedbackAsync(h.Owner,attempt,"human",new(run,"UserCorrection",h.B.Id,h.B.Variants.First().Id,"front","card-present"),default);
        var refs=await h.Db.VisualReferences.AsNoTracking().Where(x=>x.SourceAssetId==capture.AssetId).ToArrayAsync();
        Assert.Equal(promoted?1:0,refs.Length);
        if(promoted){Assert.Equal(h.B.Id,refs[0].PrintingId);Assert.True(h.Signal.Pending);Assert.Equal("promoted",f.Improvement!.Status);}
        var original=await h.Db.ScanRuns.AsNoTracking().SingleAsync(x=>x.Id==run);
        Assert.Equal(h.A.Id,VisionHistoryService.Replay(original).PrintingId);
    }

    [Fact] public async Task LaterCorrectionReplacesMemoryWithoutOverwritingOriginalPrediction()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var h=await Harness.Create(scope,true,true);var (a,r,c)=await h.Attempt(true,true);
        await h.Service.FeedbackAsync(h.Owner,a,"b",new(r,"UserCorrection",h.B.Id,null,"front","card-present"),default);
        await h.Service.FeedbackAsync(h.Owner,a,"a",new(r,"UserCorrection",h.A.Id,null,"front","card-present"),default);
        Assert.Equal(h.A.Id,(await h.Db.VisualReferences.SingleAsync(x=>x.SourceAssetId==c.AssetId)).PrintingId);
        Assert.Equal(2,await h.Db.Feedback.CountAsync(x=>x.AttemptId==a));
        await h.Service.DeleteAsync(h.Owner,a,default);
        Assert.False(await h.Db.VisualReferences.AnyAsync(x=>x.SourceAssetId==c.AssetId));
    }

    [Fact] public async Task IdenticalVerifiedBytesDoNotDuplicateMemoryAndConflictingLabelsFailClosed()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var h=await Harness.Create(scope,true,true);var (a,r,_)=await h.Attempt(true,true);
        await h.Service.FeedbackAsync(h.Owner,a,"a",new(r,"UserConfirmation",h.A.Id,null,"front","card-present"),default);
        var (b,s,_)=await h.Attempt(true,true);
        await h.Service.FeedbackAsync(h.Owner,b,"b",new(s,"UserConfirmation",h.A.Id,null,"front","card-present"),default);
        Assert.Single(await h.Db.VisualReferences.Where(x=>x.ImageSha256==h.Assets.Sha).ToArrayAsync());
        await h.Service.FeedbackAsync(h.Owner,b,"conflict",new(s,"UserCorrection",h.B.Id,null,"front","card-present"),default);
        Assert.DoesNotContain(await h.Db.VisualReferences.Where(x=>x.ImageSha256==h.Assets.Sha).ToArrayAsync(),x=>x.PrintingId==h.B.Id);
        await h.Db.SaveChangesAsync();
        Assert.False(await h.Db.ReviewedSamples.AnyAsync(x=>x.AttemptId==b && h.Db.Feedback.Any(f=>f.Id==x.FeedbackId && f.PrintingId==h.B.Id)));
    }

    [Fact] public async Task MissingStoredCaptureOrWrongSideCannotEnterMemory()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var h=await Harness.Create(scope,true,true);var (a,r,c)=await h.Attempt(true,false);
        await h.Service.FeedbackAsync(h.Owner,a,"pending",new(r,"UserConfirmation",h.A.Id,null,"front","card-present"),default);
        Assert.False(await h.Db.VisualReferences.AnyAsync(x=>x.SourceAssetId==c.AssetId));
        c.Status="ready";await h.Db.SaveChangesAsync();
        await h.Service.FeedbackAsync(h.Owner,a,"back",new(r,"UserCorrection",h.A.Id,null,"back","card-present"),default);
        Assert.False(await h.Db.VisualReferences.AnyAsync(x=>x.SourceAssetId==c.AssetId));
        await h.Service.FeedbackAsync(h.Owner,a,"front",new(r,"UserCorrection",h.A.Id,null,"front","card-present"),default);
        Assert.True(await h.Db.VisualReferences.AnyAsync(x=>x.SourceAssetId==c.AssetId));
    }
    [Fact] public async Task CaptureConfirmedAfterFeedbackRetriesPromotionWithoutAnotherHumanAction()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var h=await Harness.Create(scope,true,true);var (a,r,c)=await h.Attempt(true,false);
        var feedback=await h.Service.FeedbackAsync(h.Owner,a,"human-before-upload",new(r,"UserCorrection",h.B.Id,null,"front","card-present"),default);
        Assert.Equal("awaiting_verification",feedback.Improvement!.Status);
        await h.Service.ConfirmAsync(h.Owner,a,c.Id,new Uploads(),default);
        Assert.Equal(h.B.Id,(await h.Db.VisualReferences.SingleAsync(x=>x.SourceAssetId==c.AssetId)).PrintingId);
        Assert.Single(await h.Db.Feedback.Where(x=>x.AttemptId==a).ToArrayAsync());
    }
    [Fact] public async Task CurrentPolicyAndRetentionAreEnforcedWhenLoadingVerifiedMemory()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var h=await Harness.Create(scope,true,true);var (a,r,c)=await h.Attempt(true,true);
        await h.Service.FeedbackAsync(h.Owner,a,"human",new(r,"UserCorrection",h.B.Id,null,"front","card-present"),default);
        var reference=await h.Db.VisualReferences.AsNoTracking().SingleAsync(x=>x.SourceAssetId==c.AssetId);
        var encoder=new IndexEncoder(System.Text.Json.JsonSerializer.Deserialize<EncoderIdentity>(reference.ModelManifestJson)!);
        var index=new CosineReferenceIndex(encoder.Identity);
        var history=new VisionHistoryOptions{Enabled=true,ImprovementPolicyVersion="improve-v1"};
        var builder=new VisualReferenceBuilder(h.Db,scope.ServiceProvider.GetRequiredService<CatalogDbContext>(),scope.ServiceProvider.GetRequiredService<ISystemAssetService>(),encoder,index,scope.ServiceProvider.GetRequiredService<IClock>(),history);
        await builder.LoadAsync(default);Assert.Equal(1,index.Status.ReferenceCount);
        await builder.LoadOfficialForEvaluationAsync([],default);Assert.Equal(0,index.Status.ReferenceCount);
        history.ImprovementPolicyVersion="improve-v2";await builder.LoadAsync(default);Assert.Equal(0,index.Status.ReferenceCount);
        history.ImprovementPolicyVersion="improve-v1";var attempt=await h.Db.ScanAttempts.SingleAsync(x=>x.Id==a);attempt.RetentionUntil=DateTimeOffset.UtcNow.AddDays(-1);await h.Db.SaveChangesAsync();
        await builder.LoadAsync(default);Assert.Equal(0,index.Status.ReferenceCount);
    }
    private sealed class IndexEncoder(EncoderIdentity identity):IImageEncoder
    {public EncoderIdentity Identity=>identity;public Task<ImageEmbedding> EncodeAsync(Stream image,CancellationToken ct)=>throw new NotSupportedException();}
    private sealed class Uploads:IAssetService
    {
        public Task<Vaulta.Assets.Contracts.ConfirmAssetUploadResponse?> ConfirmUpload(Guid owner,Guid asset,CancellationToken ct)=>Task.FromResult<Vaulta.Assets.Contracts.ConfirmAssetUploadResponse?>(null);
        public Task<Vaulta.Assets.Contracts.CreateAssetUploadResponse> CreateUpload(Guid owner,Vaulta.Assets.Contracts.CreateAssetUploadRequest request,CancellationToken ct)=>throw new NotSupportedException();
        public Task<Vaulta.Assets.Contracts.CollectionAssetAccess?> GetCollectionAssetAccess(Guid owner,Guid asset,CancellationToken ct)=>throw new NotSupportedException();
        public Task<Vaulta.Assets.Contracts.CollectionAssetUrl?> CreatePrivateReadUrl(Guid owner,Guid asset,CancellationToken ct)=>throw new NotSupportedException();
    }

    private sealed class Harness
    {
        public Guid Owner=Guid.NewGuid();public required VisionDbContext Db;public required VisionHistoryService Service;
        public required Printing A,B;public required CaptureAssets Assets;public required VisualIndexRefreshSignal Signal;
        private readonly EncoderIdentity _encoder=new("fixture-"+Guid.NewGuid(),"fixture","fixture","fixture",3,"test","input","output");
        public static async Task<Harness> Create(AsyncServiceScope scope,bool enabled,bool allowed)
        {
            var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();var c=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Printing Make(){var n=Guid.NewGuid().ToString("N");return new(){Id=Guid.NewGuid(),Card=new(){Id=Guid.NewGuid(),GameId=Guid.Parse("f18fd4d1-2514-4b19-9eaa-f33c04564c7b"),Name=n,NormalizedName=n},Set=new(){Id=Guid.NewGuid(),GameId=Guid.Parse("f18fd4d1-2514-4b19-9eaa-f33c04564c7b"),Name=n,NormalizedName=n},CollectorNumber="1",NormalizedCollectorNumber="1",Language="en",Variants=[new(){Id=Guid.NewGuid(),Code="normal",Name="Normal"}]};}
            var a=Make();var b=Make();c.Printings.AddRange(a,b);await c.SaveChangesAsync();
            var owner=Guid.NewGuid();var assets=new CaptureAssets(owner);var signal=new VisualIndexRefreshSignal();
            var service=new VisionHistoryService(db,assets,scope.ServiceProvider.GetRequiredService<IVisionCatalog>(),scope.ServiceProvider.GetRequiredService<IClock>(),new(){Enabled=true,OperationalPolicyVersion="ops-v1",ImprovementPolicyVersion="improve-v1"},new(){DeveloperAutoPromote=enabled,DeveloperAccountIds=allowed?[owner]:[Guid.NewGuid()]},signal);
            return new(){Owner=owner,Db=db,Service=service,A=a,B=b,Assets=assets,Signal=signal};
        }
        public async Task<(Guid Attempt,Guid Run,Vaulta.Vision.Domain.ScanCapture Capture)> Attempt(bool consent,bool ready)
        {
            var a=await Service.CreateAsync(Owner,new("ops-v1",consent?"improve-v1":null),default);var run=await Service.ReserveAsync(Owner,a.AttemptId,"run",Assets.Sha,default);
            var prediction=new VisionScanResultDto(Guid.NewGuid(),"identified",A.Id,A.Variants.First().Id,.99,.99,[],null,"unavailable",null,new Dictionary<string,VisionFieldDto>(),new(_encoder,"fixture","fixture",null,null,1));
            await Service.CompleteAsync(run.Id,prediction,new(_encoder,[1,0,0]),default);
            var capture=new Vaulta.Vision.Domain.ScanCapture(){Id=Guid.NewGuid(),AttemptId=a.AttemptId,AssetId=Guid.NewGuid(),Sequence=0,Role="card-crop",Sha256=Assets.Sha,Status=ready?"ready":"pending",CreatedAt=DateTimeOffset.UtcNow};
            Db.ScanCaptures.Add(capture);await Db.SaveChangesAsync();return (a.AttemptId,run.Id,capture);
        }
    }
    private sealed class CaptureAssets(Guid owner):IPrivateAssetService
    {
        private readonly byte[] _bytes=Guid.NewGuid().ToByteArray();public string Sha=>Convert.ToHexString(SHA256.HashData(_bytes)).ToLowerInvariant();
        public Task<PrivateAssetDetails?> GetOwnedAsync(Guid who,Guid asset,CancellationToken ct)=>Task.FromResult<PrivateAssetDetails?>(who==owner?new(asset,"vision-scan","ready","image/png",_bytes.Length,Sha):null);
        public Task<byte[]?> ReadOwnedAsync(Guid who,Guid asset,CancellationToken ct)=>Task.FromResult<byte[]?>(who==owner?_bytes:null);
        public Task DeleteOwnedAsync(Guid who,Guid asset,CancellationToken ct)=>Task.CompletedTask;
        public Task FinalizeOwnedAsync(Guid who,Guid asset,CancellationToken ct)=>Task.CompletedTask;
    }
}
