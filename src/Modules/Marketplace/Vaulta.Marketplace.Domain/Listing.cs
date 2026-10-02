using Vaulta.SharedKernel;

namespace Vaulta.Marketplace.Domain;

public sealed record ListingCreatedDomainEvent(Guid Id, Guid ListingId, Guid SellerUserId, Guid PrintingId, Guid? VariantId, decimal PriceBrl, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record ListingUpdatedDomainEvent(Guid Id, Guid ListingId, decimal PriceBrl, string Condition, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record ListingCancelledDomainEvent(Guid Id, Guid ListingId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record ListingSoldDomainEvent(Guid Id, Guid ListingId, Guid OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Listing : AggregateRoot
{
    private readonly List<ListingPhoto> _photos = [];
    private Listing() { }

    public Guid Id { get; private set; }
    public Guid SellerUserId { get; private set; }
    public Guid CollectibleItemId { get; private set; }
    public Guid PrintingId { get; private set; }
    public Guid? VariantId { get; private set; }
    public string Condition { get; private set; } = null!;
    public decimal PriceBrl { get; private set; }
    public string Currency { get; private set; } = "BRL";
    public string Status { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? SoldAt { get; private set; }
    public Guid? SoldToOrderId { get; private set; }
    public Guid Version { get; private set; }
    public string? ClientDraftKey { get; private set; }
    public string? DraftCreationFingerprint { get; private set; }
    public string? PublicationKey { get; private set; }
    public Guid? PublicationVersion { get; private set; }
    public IReadOnlyCollection<ListingPhoto> Photos => _photos.AsReadOnly();

    public bool IsActive => Status == MarketplaceRules.ActiveStatus;

    public static Listing CreateDraft(Guid sellerUserId, Guid collectibleItemId, Guid printingId, Guid? variantId,
        string condition, decimal? priceBrl, string? description, string clientDraftKey, string fingerprint, DateTimeOffset now)
    {
        if (sellerUserId == Guid.Empty || collectibleItemId == Guid.Empty || printingId == Guid.Empty || variantId == Guid.Empty)
            throw new DomainException("Seller, collectible item and printing identifiers are required.");
        if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 64)
            throw new DomainException("Draft creation fingerprint is required.");
        return new Listing
        {
            Id = Guid.NewGuid(), SellerUserId = sellerUserId, CollectibleItemId = collectibleItemId,
            PrintingId = printingId, VariantId = variantId, Condition = MarketplaceRules.DraftCondition(condition),
            PriceBrl = priceBrl.HasValue ? MarketplaceRules.Price(priceBrl.Value) : 0,
            Description = MarketplaceRules.Description(description), Status = MarketplaceRules.DraftStatus,
            ClientDraftKey = MarketplaceRules.OperationKey(clientDraftKey), DraftCreationFingerprint = fingerprint,
            CreatedAt = now, UpdatedAt = now, Version = Guid.NewGuid()
        };
    }

    public void UpdateDraft(string condition, decimal? priceBrl, string? description, Guid expectedVersion, DateTimeOffset now)
    {
        EnsureDraft(); EnsureVersion(expectedVersion);
        var normalizedCondition = MarketplaceRules.DraftCondition(condition);
        var normalizedPrice = priceBrl.HasValue ? MarketplaceRules.Price(priceBrl.Value) : 0;
        var normalizedDescription = MarketplaceRules.Description(description);
        Condition = normalizedCondition; PriceBrl = normalizedPrice; Description = normalizedDescription; Touch(now);
    }

    public void AddDraftPhoto(Guid assetId, string type, int sortOrder, Guid expectedVersion, DateTimeOffset now)
    {
        EnsureDraft(); EnsureVersion(expectedVersion); AddPhotoCore(assetId, type, sortOrder, now);
    }

    public void RemoveDraftPhoto(Guid assetId, Guid expectedVersion, DateTimeOffset now)
    {
        EnsureDraft(); EnsureVersion(expectedVersion); RemovePhotoCore(assetId, now);
    }

    public void PrepareDraftPublication(string key, Guid expectedVersion, DateTimeOffset now)
    {
        var normalizedKey = MarketplaceRules.OperationKey(key);
        if (PublicationKey is not null)
        {
            if (PublicationKey != normalizedKey || PublicationVersion != expectedVersion)
                throw new ConflictException("Publication retry does not match the reviewed draft.");
            if (Status is not (MarketplaceRules.PublishingStatus or MarketplaceRules.ActiveStatus or MarketplaceRules.SoldStatus))
                throw new ConflictException("This publication has been cancelled.");
            return;
        }
        EnsureDraft(); EnsureVersion(expectedVersion);
        if (Condition == "UNKNOWN") throw new DomainException("Declare the condition before publishing.");
        MarketplaceRules.DraftCondition(Condition); MarketplaceRules.Price(PriceBrl);
        if (!_photos.Any(x => x.Type == "FRONT") || !_photos.Any(x => x.Type == "BACK"))
            throw new DomainException("Real front and back photos are required before publishing.");
        PublicationKey = normalizedKey; PublicationVersion = expectedVersion;
        Status = MarketplaceRules.PublishingStatus; Touch(now);
    }

    public void CancelDraft(Guid expectedVersion, DateTimeOffset now)
    {
        if (ClientDraftKey is null) throw new ConflictException("Listing is not a draft.");
        if (Status == MarketplaceRules.CancelledStatus) return;
        if (Status == MarketplaceRules.PublishingStatus) { Cancel(expectedVersion, now); return; }
        EnsureDraft(); EnsureVersion(expectedVersion); Status = MarketplaceRules.CancelledStatus; Touch(now);
    }

    public static Listing Create(
        Guid sellerUserId,
        Guid collectibleItemId,
        Guid printingId,
        Guid? variantId,
        string condition,
        decimal priceBrl,
        string? description,
        DateTimeOffset now)
    {
        if (sellerUserId == Guid.Empty || collectibleItemId == Guid.Empty || printingId == Guid.Empty)
            throw new DomainException("Seller, collectible item and printing identifiers are required.");

        var normalizedCondition = MarketplaceRules.Condition(condition);
        var normalizedPrice = MarketplaceRules.Price(priceBrl);
        var normalizedDescription = MarketplaceRules.Description(description);

        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SellerUserId = sellerUserId,
            CollectibleItemId = collectibleItemId,
            PrintingId = printingId,
            VariantId = variantId,
            Condition = normalizedCondition,
            PriceBrl = normalizedPrice,
            Description = normalizedDescription,
            Status = MarketplaceRules.ActiveStatus,
            CreatedAt = now,
            UpdatedAt = now,
            Version = Guid.NewGuid()
        };

        listing.Raise(new ListingCreatedDomainEvent(Guid.NewGuid(), listing.Id, sellerUserId, printingId, variantId, normalizedPrice, now));
        return listing;
    }

    public void Update(string condition, decimal priceBrl, string? description, Guid expectedVersion, DateTimeOffset now)
    {
        EnsureActive();
        EnsureVersion(expectedVersion);
        Condition = MarketplaceRules.Condition(condition);
        PriceBrl = MarketplaceRules.Price(priceBrl);
        Description = MarketplaceRules.Description(description);
        Touch(now);
        Raise(new ListingUpdatedDomainEvent(Guid.NewGuid(), Id, PriceBrl, Condition, now));
    }

    public void Cancel(Guid expectedVersion, DateTimeOffset now)
    {
        if (Status is not (MarketplaceRules.ActiveStatus or MarketplaceRules.PublishingStatus))
            throw new ConflictException("Somente anúncios disponíveis ou em publicação podem ser cancelados.");
        EnsureVersion(expectedVersion);
        Status = MarketplaceRules.CancelledStatus;
        Touch(now);
        Raise(new ListingCancelledDomainEvent(Guid.NewGuid(), Id, now));
    }

    public void PreparePublication(DateTimeOffset now)
    {
        EnsureActive();
        Status = MarketplaceRules.PublishingStatus;
        Touch(now);
    }

    public void Publish(DateTimeOffset now)
    {
        if (Status != MarketplaceRules.PublishingStatus) throw new ConflictException("Anúncio não está aguardando publicação.");
        Status = MarketplaceRules.ActiveStatus;
        Touch(now);
        if (ClientDraftKey is not null)
            Raise(new ListingCreatedDomainEvent(Guid.NewGuid(), Id, SellerUserId, PrintingId, VariantId, PriceBrl, now));
        else Raise(new ListingUpdatedDomainEvent(Guid.NewGuid(), Id, PriceBrl, Condition, now));
    }

    public void MarkAsSold(Guid orderId, DateTimeOffset now)
    {
        if (!IsActive) throw new DomainException("Only active listings can be marked as sold.");
        Status = MarketplaceRules.SoldStatus;
        SoldAt = now;
        SoldToOrderId = orderId;
        Touch(now);
        Raise(new ListingSoldDomainEvent(Guid.NewGuid(), Id, orderId, now));
    }

    public void AddPhoto(Guid assetId, string type, int sortOrder, DateTimeOffset now)
    {
        EnsureActive();
        AddPhotoCore(assetId, type, sortOrder, now);
    }

    private void AddPhotoCore(Guid assetId, string type, int sortOrder, DateTimeOffset now)
    {
        if (assetId == Guid.Empty) throw new DomainException("Asset identifier is required.");
        var normalizedType = MarketplaceRules.PhotoType(type);
        if (sortOrder < 0 || sortOrder > 99) throw new DomainException("Photo sort order must be between 0 and 99.");
        if (_photos.Any(x => x.AssetId == assetId)) throw new DomainException("Photo is already attached to this listing.");
        _photos.Add(new ListingPhoto(Id, assetId, normalizedType, sortOrder, _photos.Count == 0, now));
        Touch(now);
    }

    public bool ReleaseCancelledOrder(Guid orderId, DateTimeOffset now)
    {
        // A retry for an old order must never reactivate a later sale.
        if (Status != MarketplaceRules.SoldStatus || SoldToOrderId != orderId) return false;
        Status = MarketplaceRules.ActiveStatus;
        SoldAt = null;
        SoldToOrderId = null;
        Touch(now);
        Raise(new ListingUpdatedDomainEvent(Guid.NewGuid(), Id, PriceBrl, Condition, now));
        return true;
    }

    public void RemovePhoto(Guid assetId, DateTimeOffset now)
    {
        EnsureActive();
        RemovePhotoCore(assetId, now);
    }

    private void RemovePhotoCore(Guid assetId, DateTimeOffset now)
    {
        var photo = _photos.SingleOrDefault(x => x.AssetId == assetId) ?? throw new DomainException("Photo is not attached to this listing.");
        _photos.Remove(photo);
        if (photo.IsPrimary && _photos.Count > 0) _photos[0].SetPrimary(true);
        Touch(now);
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("Inactive listings cannot be modified.");
    }

    private void EnsureDraft()
    {
        if (ClientDraftKey is null || Status != MarketplaceRules.DraftStatus)
            throw new ConflictException("Only private drafts can be edited.");
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (Version != expectedVersion) throw new ConflictException("Listing changed. Reload and retry.");
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}

public sealed class ListingPhoto
{
    private ListingPhoto() { }
    internal ListingPhoto(Guid listingId, Guid assetId, string type, int sortOrder, bool isPrimary, DateTimeOffset createdAt)
    {
        ListingId = listingId; AssetId = assetId; Type = type; SortOrder = sortOrder; IsPrimary = isPrimary; CreatedAt = createdAt;
    }
    public Guid ListingId { get; private set; }
    public Guid AssetId { get; private set; }
    public string Type { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public bool IsPrimary { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    internal void SetPrimary(bool value) => IsPrimary = value;
}
