namespace Vaulta.Assets.Contracts;

public sealed record CreateAssetUploadRequest(string Purpose, string ContentType, long ContentLength, string? Sha256);
public sealed record CreateAssetUploadResponse(Guid AssetId, string UploadUrl, DateTimeOffset ExpiresAt, string ObjectKey);
public sealed record ConfirmAssetUploadResponse(Guid AssetId, string Status, string? Sha256);
public sealed record CollectionAssetAccess(Guid AssetId, Guid OwnerId, string Status, string Purpose, string Visibility, string ContentType);
public sealed record CollectionAssetUrl(Guid AssetId, string Url, DateTimeOffset ExpiresAt);
