using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Vaulta.Assets.Application;
using Vaulta.SharedKernel;
namespace Vaulta.Assets.Infrastructure;
internal sealed class PrivateAssetService(AssetsDbContext db,IAssetContentStore content,IClock clock):IPrivateAssetService
{
 public async Task<PrivateAssetDetails?> GetOwnedAsync(Guid owner,Guid asset,CancellationToken ct)
 {
  var row=await db.Assets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==asset && x.OwnerId==owner && x.Visibility=="private",ct);
  return row is null?null:new(row.Id,row.Purpose,row.Status,row.ContentType,row.ContentLength,row.Sha256);
 }
 private static void Verify(byte[]? bytes,long length,string? sha)
 {
  if(bytes is null || bytes.LongLength!=length || sha is null || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha,StringComparison.OrdinalIgnoreCase)) throw new DomainException("Private capture checksum mismatch.");
 }
 public async Task<byte[]?> ReadOwnedAsync(Guid owner,Guid asset,CancellationToken ct)
 {
  var row=await db.Assets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==asset && x.OwnerId==owner && x.Visibility=="private" && x.Status=="ready" && x.Purpose=="vision-scan",ct);
  if(row is null)return null;
  var bytes=await content.ReadAsync(row.ObjectKey,15_000_000,ct);Verify(bytes,row.ContentLength,row.Sha256);return bytes;
 }
 public async Task FinalizeOwnedAsync(Guid owner,Guid asset,CancellationToken ct)
 {
  var row=await db.Assets.SingleOrDefaultAsync(x=>x.Id==asset && x.OwnerId==owner && x.Visibility=="private" && x.Purpose=="vision-scan",ct)??throw new NotFoundException("Capture not found.");
  await db.Entry(row).ReloadAsync(ct);
  if(row.Status!="ready")throw new DomainException("Capture is not ready.");
  var bytes=await content.ReadAsync(row.ObjectKey,15_000_000,ct);Verify(bytes,row.ContentLength,row.Sha256);
  if(row.ObjectKey.StartsWith("vision-scan-final/",StringComparison.Ordinal))return;
  var original=row.ObjectKey;var sealedKey=$"vision-scan-final/{owner:N}/{asset:N}/{row.Sha256}";
  using var stream=new MemoryStream(bytes!,false);await content.PutAsync(sealedKey,stream,row.ContentType,ct);
  row.ObjectKey=sealedKey;await db.SaveChangesAsync(ct);await content.DeleteAsync(original,ct);
 }
 public async Task DeleteOwnedAsync(Guid owner,Guid asset,CancellationToken ct)
 {
  var row=await db.Assets.SingleOrDefaultAsync(x=>x.Id==asset && x.OwnerId==owner && x.Purpose=="vision-scan",ct);if(row is null)return;
  await db.Entry(row).ReloadAsync(ct);row.Status="deleted";row.DeletedAt=clock.UtcNow;await db.SaveChangesAsync(ct);
  await content.DeleteAsync(row.ObjectKey,ct);
  await content.DeleteAsync($"vision-scan/{owner:N}/{asset:N}",ct);
 }
}
