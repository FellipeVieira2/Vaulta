namespace Vaulta.Assets.Application;
public sealed record PrivateAssetDetails(Guid Id,string Purpose,string Status,string ContentType,long ContentLength,string? Sha256);
public interface IPrivateAssetService
{
 Task<PrivateAssetDetails?> GetOwnedAsync(Guid owner,Guid asset,CancellationToken ct);
 Task<byte[]?> ReadOwnedAsync(Guid owner,Guid asset,CancellationToken ct);
 Task DeleteOwnedAsync(Guid owner,Guid asset,CancellationToken ct);
}
