using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Vaulta.Assets.Application;
using Vaulta.Assets.Domain;
using Vaulta.SharedKernel;
namespace Vaulta.Assets.Infrastructure;
internal sealed class SystemAssetService(AssetsDbContext db,IAssetContentStore content,IObjectStorage urls,IClock clock) : ISystemAssetService
{
    public async Task<Guid> StoreArtworkAsync(byte[] bytes,int width,int height,string sourceUrl,bool thumbnail,CancellationToken ct)
    {
        if(bytes.Length is 0 or > 15728640 || width<1 || height<1) throw new ArgumentException("Invalid system artwork.");
        var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var key=$"catalog-artwork/{(thumbnail ? "thumbnail" : "image")}/{hash}.webp";
        var asset=await db.Assets.SingleOrDefaultAsync(x=>x.ObjectKey==key,ct);
        if(asset?.Status=="ready") { db.Entry(asset).State=EntityState.Detached; return asset.Id; }
        if(asset is null)
        {
            asset=new Asset { Id=Guid.NewGuid(),ObjectKey=key,Purpose="catalog-artwork",Visibility="private",ContentType="image/webp",ContentLength=bytes.Length,Sha256=hash,Width=width,Height=height,SourceUrl=sourceUrl,Status="pending",CreatedAt=clock.UtcNow };
            db.Assets.Add(asset); await db.SaveChangesAsync(ct);
        }
        // Stable hash/key and pending row make an interrupted PUT safely resumable.
        try
        {
            using var stream=new MemoryStream(bytes,false); await content.PutAsync(key,stream,"image/webp",ct);
            asset.Status="ready"; asset.ConfirmedAt=clock.UtcNow; await db.SaveChangesAsync(ct); return asset.Id;
        }
        finally { db.Entry(asset).State=EntityState.Detached; }
    }
    public async Task<string?> GetArtworkReadUrlAsync(Guid assetId,CancellationToken ct)
    {
        var asset=await Artwork(assetId,ct); return asset is null ? null : await urls.CreateReadUrl(asset.ObjectKey,TimeSpan.FromMinutes(5),ct);
    }
    public async Task<byte[]?> ReadArtworkAsync(Guid assetId,CancellationToken ct)
    {
        var asset=await Artwork(assetId,ct); return asset is null ? null : await content.ReadAsync(asset.ObjectKey,15*1024*1024,ct);
    }
    private Task<Asset?> Artwork(Guid id,CancellationToken ct)=>db.Assets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && x.OwnerId==null && x.Purpose=="catalog-artwork" && x.Status=="ready",ct);
}
