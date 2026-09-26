using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vaulta.Assets.Application;
using Vaulta.Assets.Contracts;
using Vaulta.Assets.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Assets.Infrastructure;

internal sealed class AssetService(AssetsDbContext db, IObjectStorage storage, IClock clock) : IAssetService
{
    public async Task<CreateAssetUploadResponse> CreateUpload(Guid ownerId, CreateAssetUploadRequest request, CancellationToken cancellationToken)
    {
        AssetRules.ValidateUpload(request.Purpose, request.ContentType, request.ContentLength, request.Sha256);

        var id = Guid.NewGuid();
        var visibility = request.Purpose == "profile-avatar" ? "public" : "private";
        var objectKey = $"{request.Purpose}/{ownerId:N}/{id:N}";
        var now = clock.UtcNow;
        var asset = new Asset
        {
            Id = id, ObjectKey = objectKey, Purpose = request.Purpose, Visibility = visibility, ContentType = request.ContentType,
            ContentLength = request.ContentLength, Sha256 = request.Sha256?.ToLowerInvariant(), OwnerId = ownerId,
            Status = "pending", CreatedAt = now
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);
        var url = await storage.CreateUploadUrl(objectKey, request.ContentType, TimeSpan.FromMinutes(10), cancellationToken);
        return new CreateAssetUploadResponse(id, url, now.AddMinutes(10), objectKey);
    }

    public async Task<ConfirmAssetUploadResponse?> ConfirmUpload(Guid ownerId, Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.SingleOrDefaultAsync(x => x.Id == assetId && x.OwnerId == ownerId, cancellationToken);
        if (asset is null || asset.Status == "deleted") return null;
        var info = await storage.GetObjectInfo(asset.ObjectKey, cancellationToken);
        if (info is null) throw new DomainException("Uploaded object was not found.");
        if (info.ContentLength != asset.ContentLength) throw new DomainException("Uploaded object size does not match the declared size.");
        if (info.ContentType is not null && !string.Equals(info.ContentType, asset.ContentType, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Uploaded object content type does not match the declared content type.");
        if (asset.Sha256 is not null && !string.Equals(asset.Sha256, info.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Uploaded object checksum does not match.");
        asset.Status = "ready";
        asset.ConfirmedAt ??= clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new ConfirmAssetUploadResponse(asset.Id, asset.Status, asset.Sha256);
    }

    public async Task<CollectionAssetAccess?> GetCollectionAssetAccess(Guid ownerId, Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId && x.OwnerId == ownerId, cancellationToken);
        return asset is null ? null : new CollectionAssetAccess(asset.Id, ownerId, asset.Status, asset.Purpose, asset.Visibility, asset.ContentType);
    }

    public async Task<CollectionAssetUrl?> CreatePrivateReadUrl(Guid ownerId, Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId && x.OwnerId == ownerId &&
            x.Status == "ready" && x.Visibility == "private" && x.ContentType.StartsWith("image/"), cancellationToken);
        if (asset is null) return null;
        var lifetime = TimeSpan.FromMinutes(5);
        var url = await storage.CreateReadUrl(asset.ObjectKey, lifetime, cancellationToken);
        return new CollectionAssetUrl(asset.Id, url, clock.UtcNow.Add(lifetime));
    }
}
