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
    public IReadOnlyCollection<ListingPhoto> Photos => _photos.AsReadOnly();

    public bool IsActive => Status == MarketplaceRules.ActiveStatus;

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
        EnsureActive();
        EnsureVersion(expectedVersion);
        Status = MarketplaceRules.CancelledStatus;
        Touch(now);
        Raise(new ListingCancelledDomainEvent(Guid.NewGuid(), Id, now));
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
        if (assetId == Guid.Empty) throw new DomainException("Asset identifier is required.");
        var normalizedType = MarketplaceRules.PhotoType(type);
        if (sortOrder < 0 || sortOrder > 99) throw new DomainException("Photo sort order must be between 0 and 99.");
        if (_photos.Any(x => x.AssetId == assetId)) throw new DomainException("Photo is already attached to this listing.");
        _photos.Add(new ListingPhoto(Id, assetId, normalizedType, sortOrder, _photos.Count == 0, now));
        Touch(now);
    }

    public void RemovePhoto(Guid assetId, DateTimeOffset now)
    {
        EnsureActive();
        var photo = _photos.SingleOrDefault(x => x.AssetId == assetId) ?? throw new DomainException("Photo is not attached to this listing.");
        _photos.Remove(photo);
        if (photo.IsPrimary && _photos.Count > 0) _photos[0].SetPrimary(true);
        Touch(now);
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("Inactive listings cannot be modified.");
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