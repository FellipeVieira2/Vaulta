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
public sealed class VisualReferencePersistenceTests(ApiFixture fixture)
{
    [Fact]
    public async Task RebuildingUnchangedArtworkDoesNotRunEncoderAgainAndModelVersionsStaySeparate()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>(); var catalog=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var name="Reference "+Guid.NewGuid().ToString("N"); var game=Guid.Parse("f18fd4d1-2514-4b19-9eaa-f33c04564c7b");
        var printing=new Printing { Id=Guid.NewGuid(),Card=new Card { Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name.ToLowerInvariant() },Set=new Set { Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name.ToLowerInvariant() },CollectorNumber="1",NormalizedCollectorNumber="1",Language="en",ArtworkAssetId=Guid.NewGuid() };
        catalog.Printings.Add(printing); await catalog.SaveChangesAsync();
        var encoder=new Encoder(Guid.NewGuid().ToString("N")); var index=new CosineReferenceIndex(encoder.Identity); var clock=scope.ServiceProvider.GetRequiredService<IClock>();
        var builder=new VisualReferenceBuilder(db,catalog,new Assets(),encoder,index,clock);
        Assert.Equal(1,(await builder.BuildAsync(printing.SetId,default)).Generated);
        Assert.Equal(1,(await builder.BuildAsync(printing.SetId,default)).Unchanged); Assert.Equal(1,encoder.Calls);
        var matches=await index.SearchAsync(new(encoder.Identity,[1f,0f,0f]),1,default); Assert.Equal(printing.Id,Assert.Single(matches).PrintingId);
        var changed=new Encoder(encoder.Identity.Revision+"-v2"); var replacement=new CosineReferenceIndex(changed.Identity);
        Assert.Equal(1,(await new VisualReferenceBuilder(db,catalog,new Assets(),changed,replacement,clock).BuildAsync(printing.SetId,default)).Generated);
        Assert.Equal(2,await db.VisualReferences.CountAsync(x=>x.PrintingId==printing.Id));
    }
    // Artificial encoder is solely a persistence/incremental-call fixture, not visual accuracy evidence.
    private sealed class Encoder(string revision) : IImageEncoder
    {
        public int Calls;
        public EncoderIdentity Identity { get; }=new("artificial-persistence-test",revision,"fixture","fixture",3,"test","input","output");
        public Task<ImageEmbedding> EncodeAsync(Stream image,CancellationToken ct) { Calls++; return Task.FromResult(new ImageEmbedding(Identity,[1f,0f,0f])); }
    }
    private sealed class Assets : ISystemAssetService
    {
        public Task<byte[]?> ReadArtworkAsync(Guid id,CancellationToken ct)=>Task.FromResult<byte[]?>([1,2,3]);
        public Task<Guid> StoreArtworkAsync(byte[] bytes,int width,int height,string source,bool thumbnail,CancellationToken ct)=>throw new NotSupportedException();
        public Task<string?> GetArtworkReadUrlAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
    }
}
