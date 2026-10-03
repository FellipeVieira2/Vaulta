namespace Vaulta.Assets.Application;
public sealed record PrivateAssetDetails(Guid Id,string Purpose,string Status,string ContentType,long ContentLength,string? Sha256);
public interface IPrivateAssetService
{
 Task<PrivateAssetDetails?> GetOwnedAsync(Guid owner,Guid asset,CancellationToken ct);
 Task<byte[]?> ReadOwnedAsync(Guid owner,Guid asset,CancellationToken ct);
 Task FinalizeOwnedAsync(Guid owner,Guid asset,CancellationToken ct)=>throw new NotSupportedException("Private asset finalization is required.");
 Task DeleteOwnedAsync(Guid owner,Guid asset,CancellationToken ct);
}
