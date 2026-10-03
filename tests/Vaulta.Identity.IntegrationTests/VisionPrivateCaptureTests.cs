using System.Security.Cryptography;
using System.Net.Http.Headers;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Vaulta.Assets.Application;
using Vaulta.SharedKernel;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Infrastructure;
using Vaulta.Vision.Application;
using Xunit;
namespace Vaulta.Identity.IntegrationTests;
[Collection("api")]
public class VisionPrivateCaptureTests(ApiFixture fixture)
{
 [Fact] public async Task ReadyOwnedCaptureRequiresMatchingRunConsentAndReviewBeforeMemoryAndCanBeWithdrawn()
 {
  var endpoint=Environment.GetEnvironmentVariable("VAULTA_TEST_MINIO_ENDPOINT")??throw new InvalidOperationException("Run with isolated MinIO endpoint.");
  var user=Environment.GetEnvironmentVariable("VAULTA_TEST_MINIO_USER")!;var password=Environment.GetEnvironmentVariable("VAULTA_TEST_MINIO_PASSWORD")!;
  using var s3=new AmazonS3Client(new BasicAWSCredentials(user,password),new AmazonS3Config {ServiceURL=endpoint,ForcePathStyle=true,AuthenticationRegion="us-east-1"});
  var bucket="vision-test-"+Guid.NewGuid().ToString("N");await s3.PutBucketAsync(new PutBucketRequest{BucketName=bucket});
  await using var factory=fixture.Factory.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Assets:S3:ServiceUrl",endpoint},{"Assets:S3:PublicServiceUrl",endpoint},{"Assets:S3:AccessKey",user},{"Assets:S3:SecretKey",password},{"Assets:S3:Bucket",bucket}})));
  await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();var assets=scope.ServiceProvider.GetRequiredService<IPrivateAssetService>();var uploads=scope.ServiceProvider.GetRequiredService<IAssetService>();var catalog=scope.ServiceProvider.GetRequiredService<IVisionCatalog>();
  var service=new VisionHistoryService(db,assets,catalog,scope.ServiceProvider.GetRequiredService<IClock>(),new(){Enabled=true,OperationalPolicyVersion="ops-v1",ImprovementPolicyVersion="improve-v1",RetentionDays=7});
  var owner=Guid.NewGuid();var attempt=await service.CreateAsync(owner,new("ops-v1","improve-v1"),default);
  var image=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");var sha=Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
  var upload=await service.UploadAsync(owner,attempt.AttemptId,new("image/png",image.Length,sha,0,"card-crop"),uploads,default);
  Assert.Null(await assets.ReadOwnedAsync(Guid.NewGuid(),upload.AssetId,default));
  await Assert.ThrowsAsync<DomainException>(()=>service.ConfirmAsync(owner,attempt.AttemptId,upload.CaptureId,uploads,default));
  using var http=new HttpClient();using var body=new ByteArrayContent(image);body.Headers.ContentType=new MediaTypeHeaderValue("image/png");(await http.PutAsync(upload.UploadUrl,body)).EnsureSuccessStatusCode();
  await service.ConfirmAsync(owner,attempt.AttemptId,upload.CaptureId,uploads,default);Assert.Equal(image,await assets.ReadOwnedAsync(owner,upload.AssetId,default));
  // The previously issued upload URL must not alter the finalized reviewed input.
  using(var changed=new ByteArrayContent(image.Concat(new byte[]{0}).ToArray())){changed.Headers.ContentType=new MediaTypeHeaderValue("image/png");(await http.PutAsync(upload.UploadUrl,changed)).EnsureSuccessStatusCode();}
  Assert.Equal(image,await assets.ReadOwnedAsync(owner,upload.AssetId,default));

  var catalogDb=scope.ServiceProvider.GetRequiredService<Vaulta.Catalog.Infrastructure.CatalogDbContext>();var unique="Memory "+Guid.NewGuid().ToString("N");var game=Guid.Parse("f18fd4d1-2514-4b19-9eaa-f33c04564c7b");
  var p=new Vaulta.Catalog.Domain.Printing {Id=Guid.NewGuid(),Card=new(){Id=Guid.NewGuid(),GameId=game,Name=unique,NormalizedName=unique.ToLowerInvariant()},Set=new(){Id=Guid.NewGuid(),GameId=game,Name=unique,NormalizedName=unique.ToLowerInvariant()},CollectorNumber="1",NormalizedCollectorNumber="1",Language="en",Variants=[new(){Id=Guid.NewGuid(),Code="normal",Name="Normal"}]};catalogDb.Printings.Add(p);await catalogDb.SaveChangesAsync();var canonical=(await catalog.GetPrintingsAsync([p.Id],default)).Single();var variant=canonical.Printing.Variants[0].Id;
  // Artificial vector tests persistence/promotion only; visual accuracy uses the separate real encoder harness.
  var identity=new EncoderIdentity("fixture","fixture","fixture","fixture",3,"test","input","output");
  var prediction=new VisionScanResultDto(Guid.NewGuid(),"identified",p.Id,variant,.9,.9,[new(canonical.Printing,.8)],null,"unavailable",null,new Dictionary<string,VisionFieldDto>(),new(identity,"fixture","fixture",null,null,1));
  var wrong=await service.ReserveAsync(owner,attempt.AttemptId,"wrong-image",new string('b',64),default);await service.CompleteAsync(wrong.Id,prediction,new(identity,[1,0,0]),default);
  var wrongFeedback=await service.FeedbackAsync(owner,attempt.AttemptId,"wrong-feedback",new(wrong.Id,"UserConfirmation",p.Id,variant,"front","card-present"),default);
  await Assert.ThrowsAsync<DomainException>(()=>service.PromoteAsync(attempt.AttemptId,wrongFeedback.Id,default));
  var run=await service.ReserveAsync(owner,attempt.AttemptId,"correct-image",sha,default);await service.CompleteAsync(run.Id,prediction,new(identity,[1,0,0]),default);
  Assert.False(await db.VisualReferences.AnyAsync(x=>x.SourceAssetId==upload.AssetId));
  var feedback=await service.FeedbackAsync(owner,attempt.AttemptId,"confirmed",new(run.Id,"UserConfirmation",p.Id,variant,"front","card-present"),default);
  var sample=await service.PromoteAsync(attempt.AttemptId,feedback.Id,default);Assert.Equal(sample,await service.PromoteAsync(attempt.AttemptId,feedback.Id,default));
  Assert.Single(await db.VisualReferences.Where(x=>x.SourceAssetId==upload.AssetId).ToArrayAsync());
  var frozen=await service.ExportAsync("frozen-"+owner.ToString("N"),default);Assert.Contains(frozen.Samples,x=>x.SampleId==sample);Assert.DoesNotContain(owner.ToString(),System.Text.Json.JsonSerializer.Serialize(frozen));Assert.Equal(System.Text.Json.JsonSerializer.Serialize(frozen),System.Text.Json.JsonSerializer.Serialize(await service.ExportAsync(frozen.DatasetVersion,default)));
  await service.DeleteAsync(owner,attempt.AttemptId,default);Assert.Null(await assets.ReadOwnedAsync(owner,upload.AssetId,default));Assert.False(await db.VisualReferences.AnyAsync(x=>x.SourceAssetId==upload.AssetId));
  await Assert.ThrowsAsync<ConflictException>(()=>service.ExportAsync(frozen.DatasetVersion,default));Assert.All((await service.GetAsync(owner,attempt.AttemptId,default)).Runs,r=>Assert.False(r.HasEmbedding));
 }
}
