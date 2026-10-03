using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Assets.Application;
using Vaulta.Assets.Domain;
using Vaulta.Assets.Infrastructure;
using Vaulta.SharedKernel;
using Xunit;
namespace Vaulta.Identity.IntegrationTests;
[Collection("api")]
public sealed class SystemArtworkPersistenceTests(ApiFixture fixture)
{
    [Fact]
    public async Task InterruptedStorageRetriesPendingAssetWithSameIdentityAndDoesNotUploadItTwice()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<AssetsDbContext>(); var storage=new Storage();
        var service=new SystemAssetService(db,storage,storage,scope.ServiceProvider.GetRequiredService<IClock>());
        var bytes=Guid.NewGuid().ToByteArray(); var url="https://assets.tcgdex.net/en/base/base1/1/high.png";
        await Assert.ThrowsAsync<IOException>(()=>service.StoreArtworkAsync(bytes,30,40,url,false,default));
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var pending=await db.Assets.AsNoTracking().SingleAsync(x=>x.Sha256==hash); Assert.Equal("pending",pending.Status);
        storage.Fail=false; var id=await service.StoreArtworkAsync(bytes,30,40,url,false,default);
        Assert.Equal(pending.Id,id); Assert.Equal(id,await service.StoreArtworkAsync(bytes,30,40,url,false,default));
        Assert.Equal(2,storage.Puts); Assert.Equal(bytes,await service.ReadArtworkAsync(id,default));
        Assert.NotNull(await service.GetArtworkReadUrlAsync(id,default));
        var ready=await db.Assets.AsNoTracking().SingleAsync(x=>x.Id==id); Assert.Null(ready.OwnerId); Assert.Equal("private",ready.Visibility);
    }
    [Fact]
    public async Task PublicArtworkPortCannotReadUserOwnedPrivateImages()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        var asset=new Asset { Id=Guid.NewGuid(),ObjectKey="user/"+Guid.NewGuid(),Purpose="collection-item",Visibility="private",ContentType="image/png",ContentLength=4,OwnerId=Guid.NewGuid(),Status="ready",CreatedAt=DateTimeOffset.UtcNow };
        db.Assets.Add(asset); await db.SaveChangesAsync(); var storage=new Storage();
        var service=new SystemAssetService(db,storage,storage,scope.ServiceProvider.GetRequiredService<IClock>());
        Assert.Null(await service.ReadArtworkAsync(asset.Id,default)); Assert.Null(await service.GetArtworkReadUrlAsync(asset.Id,default));
    }
    private sealed class Storage : IAssetContentStore,IObjectStorage
    {
        public bool Fail=true; public int Puts; private readonly Dictionary<string,byte[]> _objects=new();
        public async Task PutAsync(string key,Stream bytes,string type,CancellationToken ct)
        { Puts++; if(Fail) throw new IOException("Interrupted fixture PUT"); using var output=new MemoryStream(); await bytes.CopyToAsync(output,ct); _objects[key]=output.ToArray(); }
        public Task<byte[]?> ReadAsync(string key,int max,CancellationToken ct)=>Task.FromResult(_objects.GetValueOrDefault(key));
        public Task DeleteAsync(string key,CancellationToken ct) { _objects.Remove(key); return Task.CompletedTask; }
        public Task<string> CreateReadUrl(string key,TimeSpan lifetime,CancellationToken ct)=>Task.FromResult("https://internal-fixture/"+key);
        public Task<string> CreateUploadUrl(string key,string type,TimeSpan lifetime,CancellationToken ct)=>throw new NotSupportedException();
        public Task<StoredObjectInfo?> GetObjectInfo(string key,CancellationToken ct)=>throw new NotSupportedException();
    }
}
