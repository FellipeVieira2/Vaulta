using Vaulta.Assets.Contracts;

namespace Vaulta.Assets.Application;

public interface IObjectStorage
{
    Task<string> CreateUploadUrl(string objectKey, string contentType, TimeSpan lifetime, CancellationToken cancellationToken);
    Task<StoredObjectInfo?> GetObjectInfo(string objectKey, CancellationToken cancellationToken);
    Task<string> CreateReadUrl(string objectKey, TimeSpan lifetime, CancellationToken cancellationToken);
}

public sealed record StoredObjectInfo(long ContentLength, string? ContentType, string? Sha256);

public interface IAssetService
{
    Task<CreateAssetUploadResponse> CreateUpload(Guid ownerId, CreateAssetUploadRequest request, CancellationToken cancellationToken);
    Task<ConfirmAssetUploadResponse?> ConfirmUpload(Guid ownerId, Guid assetId, CancellationToken cancellationToken);
    Task<CollectionAssetAccess?> GetCollectionAssetAccess(Guid ownerId, Guid assetId, CancellationToken cancellationToken);
    Task<CollectionAssetUrl?> CreatePrivateReadUrl(Guid ownerId, Guid assetId, CancellationToken cancellationToken);
}

// Internal server-only port. It is not exposed by the user upload endpoint.
public interface IAssetContentStore
{
    Task PutAsync(string key,Stream content,string contentType,CancellationToken ct);
    Task<byte[]?> ReadAsync(string key,int maxBytes,CancellationToken ct);
    Task DeleteAsync(string key,CancellationToken ct);
}
public interface ISystemAssetService
{
    Task<Guid> StoreArtworkAsync(byte[] bytes,int width,int height,string sourceUrl,bool thumbnail,CancellationToken ct);
    Task<string?> GetArtworkReadUrlAsync(Guid assetId,CancellationToken ct);
    Task<byte[]?> ReadArtworkAsync(Guid assetId,CancellationToken ct);
}
