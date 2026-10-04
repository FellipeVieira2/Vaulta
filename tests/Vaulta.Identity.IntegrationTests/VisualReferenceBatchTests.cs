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
public sealed class VisualReferenceBatchTests(ApiFixture fixture)
{
    [Fact] public async Task ConcurrentFullBuildLockDefersBatchWithoutReadingAssets()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var catalog=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var assets=new Assets();var encoder=new Encoder();var printing=Printing(assets.Add());catalog.Printings.Add(printing);await catalog.SaveChangesAsync();
        await using var lockScope=fixture.Factory.Services.CreateAsyncScope();var lockDb=lockScope.ServiceProvider.GetRequiredService<VisionDbContext>();await lockDb.Database.OpenConnectionAsync();
        var key=BitConverter.ToInt64(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("vaulta:vision:index:"+VisualReferenceBuilder.ModelVersion(encoder.Identity))),0);
        await lockDb.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock({0})",key);
        try{var result=await Builder(db,catalog,assets,encoder,printing.Set.Id).BuildReadyBatchAsync(1,TimeSpan.FromMinutes(5),default);Assert.True(result.Busy);Assert.Null(result.RemainingReady);Assert.Equal(0,encoder.Calls);Assert.Equal(0,assets.Reads);}
        finally{await lockDb.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})",key);await lockDb.Database.CloseConnectionAsync();}
    }
    [Fact] public async Task BatchesOnlyReadyImagesAndPicksUpLaterImagesWithoutRepeatingVectors()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var catalog=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var assets=new Assets();var encoder=new Encoder();var ready=Printing(assets.Add());var late=Printing(null);late.Set=ready.Set;catalog.Printings.AddRange(ready,late);await catalog.SaveChangesAsync();
        var builder=Builder(db,catalog,assets,encoder,ready.Set.Id);
        var first=await builder.BuildReadyBatchAsync(1,TimeSpan.FromMinutes(5),default);Assert.Equal(1,first.Generated);Assert.Equal(0,first.RemainingReady);
        Assert.Equal(0,(await builder.BuildReadyBatchAsync(1,TimeSpan.FromMinutes(5),default)).Generated);Assert.Equal(1,encoder.Calls);
        late.ArtworkAssetId=assets.Add();late.ArtworkImportStatus="ready";await catalog.SaveChangesAsync();
        Assert.Equal(1,(await builder.BuildReadyBatchAsync(1,TimeSpan.FromMinutes(5),default)).Generated);Assert.Equal(2,encoder.Calls);
        Assert.Equal(2,await db.VisualReferences.CountAsync(x=>x.ModelVersion==VisualReferenceBuilder.ModelVersion(encoder.Identity)));
    }
    [Fact] public async Task BoundsBatchAndReusesSameAssetAcrossPrintings()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var catalog=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var assets=new Assets();var encoder=new Encoder();var asset=assets.Add();var one=Printing(asset);var two=Printing(asset);two.Set=one.Set;catalog.Printings.AddRange(one,two);await catalog.SaveChangesAsync();var builder=Builder(db,catalog,assets,encoder,one.Set.Id);
        Assert.Equal(1,(await builder.BuildReadyBatchAsync(1,TimeSpan.FromMinutes(5),default)).Generated);
        var second=await builder.BuildReadyBatchAsync(1,TimeSpan.FromMinutes(5),default);Assert.Equal(1,second.Reused);Assert.Equal(0,second.RemainingReady);Assert.Equal(1,encoder.Calls);Assert.Equal(1,assets.Reads);
    }
    [Fact] public async Task FailedImageUsesBackoffWhileOtherImagesContinue()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var catalog=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var assets=new Assets();var encoder=new Encoder();var bad=Printing(Guid.NewGuid());catalog.Printings.Add(bad);await catalog.SaveChangesAsync();var builder=Builder(db,catalog,assets,encoder,bad.Set.Id);
        Assert.Equal(1,(await builder.BuildReadyBatchAsync(1,TimeSpan.FromMinutes(5),default)).Failed);
        var good=Printing(assets.Add());good.Set=bad.Set;catalog.Printings.Add(good);await catalog.SaveChangesAsync();var next=await builder.BuildReadyBatchAsync(1,TimeSpan.FromMinutes(5),default);Assert.Equal(1,next.Generated);Assert.Equal(0,next.Failed);Assert.Equal(1,next.RemainingReady);
        Assert.Equal(0,(await builder.BuildReadyBatchAsync(1,TimeSpan.FromMinutes(5),default)).Failed);
    }
    private static ScopedBuilder Builder(VisionDbContext db,CatalogDbContext catalog,Assets assets,Encoder encoder,Guid set)=>new(new(db,catalog,assets,encoder,new CosineReferenceIndex(encoder.Identity),new Clock()),set);
    private sealed class ScopedBuilder(VisualReferenceBuilder builder,Guid set)
    {public Task<VisualReferenceBatchReport> BuildReadyBatchAsync(int count,TimeSpan retry,CancellationToken ct)=>builder.BuildReadyBatchAsync(count,retry,ct,set);}
    private static Printing Printing(Guid? asset)
    {var name="Batch "+Guid.NewGuid().ToString("N");var number=Guid.NewGuid().ToString("N");var game=Guid.Parse("f18fd4d1-2514-4b19-9eaa-f33c04564c7b");return new(){Id=Guid.NewGuid(),ArtworkAssetId=asset,ArtworkImportStatus=asset.HasValue?"ready":null,Card=new(){Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name},Set=new(){Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name},CollectorNumber=number,NormalizedCollectorNumber=number,Language="en"};}
    private sealed class Clock:IClock {public DateTimeOffset UtcNow=>DateTimeOffset.UtcNow;}
    private sealed class Encoder:IImageEncoder
    {public int Calls;public EncoderIdentity Identity{get;}=new("batch-"+Guid.NewGuid(),"fixture","fixture","fixture",3,"test","input","output");public Task<ImageEmbedding> EncodeAsync(Stream image,CancellationToken ct){Calls++;return Task.FromResult(new ImageEmbedding(Identity,[1,0,0]));}}
    private sealed class Assets:ISystemAssetService
    {private readonly Dictionary<Guid,byte[]> _images=[];public int Reads;public Guid Add(){var id=Guid.NewGuid();_images[id]=id.ToByteArray();return id;}public Task<byte[]?> ReadArtworkAsync(Guid id,CancellationToken ct){Reads++;return Task.FromResult(_images.GetValueOrDefault(id));}public Task<Guid> StoreArtworkAsync(byte[] bytes,int w,int h,string source,bool thumbnail,CancellationToken ct)=>throw new NotSupportedException();public Task<string?> GetArtworkReadUrlAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();}
}
