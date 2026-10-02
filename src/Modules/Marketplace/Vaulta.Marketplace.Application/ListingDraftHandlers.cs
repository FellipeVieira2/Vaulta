using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vaulta.Assets.Contracts;
using Vaulta.Marketplace.Application.Commands;
using Vaulta.Marketplace.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Marketplace.Application;

public sealed class ListingDraftHandlers(IMarketplaceStore store, IMarketplaceCatalog catalog,
    IMarketplaceCollection collection, IMarketplaceAssets assets, ListingPublicationService publication, IClock clock)
{
    public async Task<ListingDraftDto> Handle(CreateListingDraftCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var normalized = request with
        {
            ClientDraftKey = MarketplaceRules.OperationKey(request.ClientDraftKey),
            Condition = MarketplaceRules.DraftCondition(request.Condition),
            PriceBrl = request.PriceBrl.HasValue ? MarketplaceRules.Price(request.PriceBrl.Value) : null,
            Description = MarketplaceRules.Description(request.Description)
        };
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized))));
        var existing = await store.FindDraftByKey(command.UserId, normalized.ClientDraftKey, ct);
        if (existing is not null) return await ReplayCreate(existing, fingerprint, ct);
        var draft = Listing.CreateDraft(command.UserId, normalized.CollectibleItemId, normalized.PrintingId, normalized.VariantId,
            normalized.Condition, normalized.PriceBrl, normalized.Description, normalized.ClientDraftKey, fingerprint, clock.UtcNow);
        await ValidateUnit(draft, requireCondition: false, ct);
        await ValidateCatalog(draft, ct);
        var persisted = await store.CreateDraft(draft, ct);
        return await ReplayCreate(persisted, fingerprint, ct);
    }

    public async Task<ListingDraftDto> Get(Guid userId, Guid draftId, CancellationToken ct) => await Map(await RequireDraft(userId, draftId, ct), ct);

    public async Task<ListingDraftDto> Handle(UpdateListingDraftCommand command, CancellationToken ct)
    {
        var draft = await RequireDraft(command.UserId, command.DraftId, ct);
        draft.UpdateDraft(command.Request.Condition, command.Request.PriceBrl, command.Request.Description, command.Request.Version, clock.UtcNow);
        await store.Save(ct); return await Map(draft, ct);
    }

    public async Task<ListingDraftDto> Handle(AddListingDraftPhotoCommand command, CancellationToken ct)
    {
        var draft = await RequireDraft(command.UserId, command.DraftId, ct);
        await ValidateAsset(command.UserId, command.Request.AssetId, ct);
        draft.AddDraftPhoto(command.Request.AssetId, command.Request.Type, command.Request.SortOrder, command.Request.Version, clock.UtcNow);
        await store.Save(ct); return await Map(draft, ct);
    }

    public async Task<ListingDraftDto> Handle(RemoveListingDraftPhotoCommand command, CancellationToken ct)
    {
        var draft = await RequireDraft(command.UserId, command.DraftId, ct);
        draft.RemoveDraftPhoto(command.AssetId, command.Version, clock.UtcNow);
        await store.Save(ct); return await Map(draft, ct);
    }

    public async Task<ListingDraftDto> Handle(PublishListingDraftCommand command, CancellationToken ct)
    {
        var draft = await RequireDraft(command.UserId, command.DraftId, ct);
        var firstAttempt = draft.PublicationKey is null;
        draft.PrepareDraftPublication(command.Request.IdempotencyKey, command.Request.Version, clock.UtcNow);
        if (firstAttempt)
        {
            var seller = await store.FindSellerProfile(command.UserId, ct) ?? throw new NotFoundException("Enable selling before publishing.");
            if (!seller.IsActive) throw new ConflictException("Seller profile is suspended.");
            await ValidateUnit(draft, requireCondition: true, ct);
            await ValidateCatalog(draft, ct);
            foreach (var photo in draft.Photos) await ValidateAsset(command.UserId, photo.AssetId, ct);
            // Persist the reviewed payload before reserving; the existing worker recovers an interrupted publication.
            await store.Save(ct);
        }
        await publication.Publish(draft, ct);
        return await Map(draft, ct);
    }

    public async Task<ListingDraftDto> Handle(CancelListingDraftCommand command, CancellationToken ct)
    {
        var draft = await RequireDraft(command.UserId, command.DraftId, ct);
        var held = draft.PublicationKey is not null;
        draft.CancelDraft(command.Version, clock.UtcNow);
        await store.Save(ct);
        if (held) await collection.ReleaseListingItem(draft, ct);
        return await Map(draft, ct);
    }

    private async Task<Listing> RequireDraft(Guid user, Guid id, CancellationToken ct)
    {
        var listing = await store.FindListing(user, id, ct);
        return listing?.ClientDraftKey is not null ? listing : throw new NotFoundException("Listing draft not found.");
    }

    private async Task<ListingDraftDto> ReplayCreate(Listing draft, string fingerprint, CancellationToken ct)
    {
        if (draft.DraftCreationFingerprint != fingerprint) throw new ConflictException("Draft key was already used with a different request.");
        return await Map(draft, ct);
    }

    private async Task ValidateUnit(Listing draft, bool requireCondition, CancellationToken ct)
    {
        var item = await collection.GetCollectibleItem(draft.SellerUserId, draft.CollectibleItemId, ct)
            ?? throw new NotFoundException("Collectible item not found or does not belong to this user.");
        if (!string.Equals(item.Status, MarketplaceRules.ActiveStatus, StringComparison.OrdinalIgnoreCase)
            || item.ListedById is not null && item.ListedById != draft.Id)
            throw new ConflictException("Only available collectible items can be listed.");
        var identity = await collection.GetItemIdentity(draft.SellerUserId, item.CollectionEntryId, ct);
        if (identity is null || identity.PrintingId != draft.PrintingId || identity.VariantId != draft.VariantId)
            throw new ConflictException("Printing or variant does not match the collectible unit.");
        if (requireCondition && item.Condition != draft.Condition)
            throw new ConflictException("Declared condition does not match the collectible unit.");
    }

    private async Task ValidateCatalog(Listing draft, CancellationToken ct)
    {
        var printing = await catalog.GetPrinting(draft.PrintingId, ct) ?? throw new NotFoundException("Printing not found.");
        if (!printing.IsActive) throw new ConflictException("Printing is no longer available in the catalog.");
        if (draft.VariantId is not { } variantId) return;
        var variant = await catalog.GetVariant(variantId, ct) ?? throw new NotFoundException("Variant not found.");
        if (variant.PrintingId != printing.PrintingId) throw new DomainException("Variant does not belong to the printing.");
        if (!variant.IsActive) throw new ConflictException("Variant is no longer available in the catalog.");
    }

    private async Task<CollectionAssetAccess> ValidateAsset(Guid owner, Guid id, CancellationToken ct)
    {
        var asset = await assets.GetAccess(owner, id, ct) ?? throw new NotFoundException("Asset not found.");
        if (asset.OwnerId != owner) throw new ForbiddenException("Asset does not belong to this user.");
        if (asset.Status != "ready" || asset.Visibility != "private" || asset.Purpose != "collection-item"
            || !asset.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Only ready private photos of the owned unit can be attached.");
        return asset;
    }

    private async Task<ListingDraftDto> Map(Listing draft, CancellationToken ct)
    {
        var photos = new List<ListingPhotoDto>();
        foreach (var photo in draft.Photos.OrderBy(x => x.SortOrder))
        {
            var url = await assets.GetUrl(draft.SellerUserId, photo.AssetId, ct);
            photos.Add(new(photo.AssetId, photo.Type, photo.SortOrder, photo.IsPrimary, url?.Url ?? "", url?.ExpiresAt ?? DateTimeOffset.MinValue));
        }
        return new(draft.Id, draft.ClientDraftKey!, draft.SellerUserId, draft.CollectibleItemId, draft.PrintingId, draft.VariantId,
            draft.Condition, draft.PriceBrl == 0 ? null : draft.PriceBrl, draft.Currency, draft.Status, draft.Description, photos, draft.CreatedAt, draft.Version);
    }
}
