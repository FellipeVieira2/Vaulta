using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Infrastructure;
using Vaulta.SharedKernel;
using Xunit;
namespace Vaulta.Identity.IntegrationTests;
[Collection("api")]
public class BenchmarkIndexIsolationTests(ApiFixture fixture)
{
 [Fact] public async Task HeldOutAssetsNeverReachTheEvaluationIndex()
 {
  var method=typeof(VisualReferenceBuilder).GetMethod("LoadForEvaluationAsync");Assert.NotNull(method);
  await using var scope=fixture.Factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();var catalog=scope.ServiceProvider.GetRequiredService<Vaulta.Catalog.Infrastructure.CatalogDbContext>();
  var asset=Guid.NewGuid();var name="Heldout "+Guid.NewGuid().ToString("N");var game=Guid.Parse("f18fd4d1-2514-4b19-9eaa-f33c04564c7b");
  var printing=new Vaulta.Catalog.Domain.Printing{Id=Guid.NewGuid(),ArtworkAssetId=asset,Card=new(){Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name.ToLowerInvariant()},Set=new(){Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name.ToLowerInvariant()},CollectorNumber="1",NormalizedCollectorNumber="1",Language="en"};catalog.Printings.Add(printing);await catalog.SaveChangesAsync();
  var encoder=new Encoder();db.VisualReferences.Add(new(){Id=Guid.NewGuid(),PrintingId=printing.Id,SourceAssetId=asset,Origin="official",Status="ready",ModelVersion=VisualReferenceBuilder.ModelVersion(encoder.Identity),ModelManifestJson=System.Text.Json.JsonSerializer.Serialize(encoder.Identity),Dimension=3,Vector=[1,0,0],VectorSha256=new string('a',64),CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
  var index=new CosineReferenceIndex(encoder.Identity,1000);var builder=new VisualReferenceBuilder(db,catalog,scope.ServiceProvider.GetRequiredService<Vaulta.Assets.Application.ISystemAssetService>(),encoder,index,scope.ServiceProvider.GetRequiredService<IClock>());await builder.LoadAsync(default);Assert.Equal(1,index.Status.ReferenceCount);
  await (Task<VisualIndexStatus>)method.Invoke(builder,[new Guid[]{asset},CancellationToken.None])!;Assert.Equal(0,index.Status.ReferenceCount);
 }
 private sealed class Encoder:IImageEncoder
 {public EncoderIdentity Identity=>new("heldout","fixture","fixture","fixture",3,"test","input","output");public Task<ImageEmbedding> EncodeAsync(Stream image,CancellationToken ct)=>Task.FromResult(new ImageEmbedding(Identity,[1,0,0]));}
}
