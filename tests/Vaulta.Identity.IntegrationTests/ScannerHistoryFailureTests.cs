using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Infrastructure;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.SharedKernel;
using Vaulta.Web.Api;
using Xunit;
namespace Vaulta.Identity.IntegrationTests;
[Collection("api")]
public class ScannerHistoryFailureTests(ApiFixture fixture)
{
 [Fact] public async Task ReservationStorageFailurePreservesUnsavedRecognitionWithoutBypassingOwnership()
 {
  await using var scope=fixture.Factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();var owner=Guid.NewGuid();var catalog=new Catalog();var clock=scope.ServiceProvider.GetRequiredService<IClock>();var assets=scope.ServiceProvider.GetRequiredService<Vaulta.Assets.Application.IPrivateAssetService>();var options=new VisionHistoryOptions{Enabled=true,OperationalPolicyVersion="ops-v1"};
  var attempt=await new VisionHistoryService(db,assets,catalog,clock,options).CreateAsync(owner,new("ops-v1"),default);
  await using var broken=new VisionDbContext(new DbContextOptionsBuilder<VisionDbContext>().UseNpgsql(db.Database.GetConnectionString()).AddInterceptors(new FailWrites()).Options);
  var history=new VisionHistoryService(broken,assets,catalog,clock,options);var encoder=new Encoder();var prices=new Prices();
  var scanner=new VisionScannerService(encoder,new Index(encoder.Identity),catalog,new Evidence(),prices,new());
  var coordinator=new ScannerVisionCoordinator(scanner,history,broken,prices,NullLogger<ScannerVisionCoordinator>.Instance);
  var result=await coordinator.IdentifyAsync(owner,[1],null,attempt.AttemptId,"exec",default);Assert.Equal("not_a_card",result.Status);Assert.Equal("failed",result.History!.PersistenceStatus);
  await Assert.ThrowsAsync<NotFoundException>(()=>coordinator.IdentifyAsync(Guid.NewGuid(),[1],null,attempt.AttemptId,"other",default));
 }
 private sealed class FailWrites:SaveChangesInterceptor
 {public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,InterceptionResult<int> result,CancellationToken ct=default)=>throw new DbUpdateException("Simulated history storage failure.");}
 private sealed class Encoder:IImageEncoder
 {public EncoderIdentity Identity=>new("fixture","fixture","fixture","fixture",3,"test","input","output");public Task<ImageEmbedding> EncodeAsync(Stream image,CancellationToken ct)=>Task.FromResult(new ImageEmbedding(Identity,[1,0,0]));}
 private sealed class Index(EncoderIdentity identity):IVisualReferenceIndex
 {public VisualIndexStatus Status=>new("fixture",0,identity);public Task<IReadOnlyList<VisualMatch>> SearchAsync(ImageEmbedding image,int topK,CancellationToken ct)=>Task.FromResult<IReadOnlyList<VisualMatch>>([]);}
 private sealed class Catalog:IVisionCatalog
 {public Task<IReadOnlyList<VisionCatalogPrinting>> GetPrintingsAsync(IReadOnlyList<Guid> ids,CancellationToken ct)=>Task.FromResult<IReadOnlyList<VisionCatalogPrinting>>([]);public Task<IReadOnlyList<VisionCatalogPrinting>> FindEvidenceCandidatesAsync(CardEvidence e,CancellationToken ct)=>Task.FromResult<IReadOnlyList<VisionCatalogPrinting>>([]);}
 private sealed class Evidence:IVisionEvidenceReader
 {public Task<VisionEvidenceReading> ReadAsync(byte[] image,IReadOnlyList<VisionCatalogPrinting> candidates,CancellationToken ct)=>Task.FromResult(new VisionEvidenceReading(new(new(null,0),new(null,0),new(null,0),new(null,0),new(null,0),new(null,0),new(null,0),"fixture","fixture",IsCard:new("false",.95)),null,null));}
 private sealed class Prices:IScannerCardDetailsReader
 {public Task<ScannerCardDetailsDto?> GetAsync(Guid id,CancellationToken ct)=>Task.FromResult<ScannerCardDetailsDto?>(null);}
}
