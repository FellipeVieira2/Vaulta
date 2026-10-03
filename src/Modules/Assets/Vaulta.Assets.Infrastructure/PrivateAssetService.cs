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
 public async Task<byte[]?> ReadOwnedAsync(Guid owner,Guid asset,CancellationToken ct)
 {
  var row=await db.Assets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==asset && x.OwnerId==owner && x.Visibility=="private" && x.Status=="ready" && x.Purpose=="vision-scan",ct);
  return row is null?null:await content.ReadAsync(row.ObjectKey,15_000_000,ct);
 }
 public async Task DeleteOwnedAsync(Guid owner,Guid asset,CancellationToken ct)
 {
  var row=await db.Assets.SingleOrDefaultAsync(x=>x.Id==asset && x.OwnerId==owner && x.Purpose=="vision-scan",ct);if(row is null) return;
  await content.DeleteAsync(row.ObjectKey,ct);row.Status="deleted";row.DeletedAt=clock.UtcNow;await db.SaveChangesAsync(ct);
 }
}
