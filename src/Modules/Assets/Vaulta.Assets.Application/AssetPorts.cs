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
