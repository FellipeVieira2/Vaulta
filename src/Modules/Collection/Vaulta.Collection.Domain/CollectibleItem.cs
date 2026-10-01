using Vaulta.SharedKernel;

namespace Vaulta.Collection.Domain;

public sealed record CollectibleItemUpdatedDomainEvent(Guid Id, Guid CollectibleItemId, Guid CollectionEntryId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record CollectibleItemRemovedDomainEvent(Guid Id, Guid CollectibleItemId, Guid CollectionEntryId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record CollectibleItemAssetChangedDomainEvent(Guid Id, Guid CollectibleItemId, Guid AssetId, bool Attached, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record CollectibleItemSoldDomainEvent(Guid Id, Guid CollectibleItemId, Guid OrderId, Guid BuyerItemId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class CollectibleItem : AggregateRoot
{
    private readonly List<CollectibleItemAsset> _assets = [];
    private CollectibleItem() { }

    public Guid Id { get; private set; }
    public Guid CollectionEntryId { get; private set; }
    public Guid UserId { get; private set; }
    public string Condition { get; private set; } = null!;
    public decimal? AcquisitionAmount { get; private set; }
    public string? AcquisitionCurrency { get; private set; }
    public DateOnly? AcquisitionDate { get; private set; }
    public string? Notes { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public Guid Version { get; private set; }
    public Guid? ListedById { get; private set; }
    public IReadOnlyCollection<CollectibleItemAsset> Assets => _assets.AsReadOnly();

    public static CollectibleItem Create(Guid entryId, Guid userId, string condition, Money? price, DateOnly? acquisitionDate, string? notes, DateTimeOffset now)
    {
        if (entryId == Guid.Empty || userId == Guid.Empty) throw new DomainException("Entry and user identifiers are required.");
        var normalizedCondition = CollectionRules.Condition(condition);
        var normalizedNotes = CollectionRules.Notes(notes);
        return new CollectibleItem
        {
            Id = Guid.NewGuid(), CollectionEntryId = entryId, UserId = userId, Condition = normalizedCondition,
            AcquisitionAmount = price?.Amount, AcquisitionCurrency = price?.Currency, AcquisitionDate = acquisitionDate,
            Notes = normalizedNotes, Status = CollectionRules.ActiveStatus, CreatedAt = now, UpdatedAt = now, Version = Guid.NewGuid()
        };
    }

    public Money? AcquisitionPrice => AcquisitionAmount is null || AcquisitionCurrency is null ? null : new Money(AcquisitionAmount.Value, AcquisitionCurrency);
    public bool IsActive => Status == CollectionRules.ActiveStatus;

    public void Update(string condition, Money? price, DateOnly? acquisitionDate, string? notes, Guid expectedVersion, DateTimeOffset now)
    {
        EnsureActive();
        EnsureVersion(expectedVersion);
        CollectionRules.ValidateAcquisitionPrice(price);
        Condition = CollectionRules.Condition(condition);
        AcquisitionAmount = price?.Amount;
        AcquisitionCurrency = price?.Currency;
        AcquisitionDate = acquisitionDate;
        Notes = CollectionRules.Notes(notes);
        Touch(now);
        Raise(new CollectibleItemUpdatedDomainEvent(Guid.NewGuid(), Id, CollectionEntryId, now));
    }

    public void Remove(Guid expectedVersion, DateTimeOffset now)
    {
        EnsureActive();
        EnsureVersion(expectedVersion);
        Status = CollectionRules.RemovedStatus;
        RemovedAt = now;
        Touch(now);
        Raise(new CollectibleItemRemovedDomainEvent(Guid.NewGuid(), Id, CollectionEntryId, now));
    }

    public void AttachAsset(Guid assetId, string type, int sortOrder, DateTimeOffset now)
    {
        EnsureActive();
        if (assetId == Guid.Empty) throw new DomainException("Asset identifier is required.");
        var normalizedType = CollectionRules.AssetType(type);
        if (sortOrder < 0 || sortOrder > 99) throw new DomainException("Asset sort order must be between 0 and 99.");
        if (_assets.Any(x => x.AssetId == assetId)) throw new DomainException("Asset is already attached to this item.");
        _assets.Add(new CollectibleItemAsset(Id, assetId, normalizedType, sortOrder, _assets.Count == 0, now));
        Touch(now);
        Raise(new CollectibleItemAssetChangedDomainEvent(Guid.NewGuid(), Id, assetId, true, now));
    }

    public void RemoveAsset(Guid assetId, DateTimeOffset now)
    {
        EnsureActive();
        var asset = _assets.SingleOrDefault(x => x.AssetId == assetId) ?? throw new DomainException("Asset is not attached to this item.");
        _assets.Remove(asset);
        if (asset.IsPrimary && _assets.Count > 0) _assets[0].SetPrimary(true);
        Touch(now);
        Raise(new CollectibleItemAssetChangedDomainEvent(Guid.NewGuid(), Id, assetId, false, now));
    }

    public void SetPrimaryAsset(Guid assetId, DateTimeOffset now)
    {
        EnsureActive();
        if (!_assets.Any(x => x.AssetId == assetId)) throw new DomainException("Only an asset attached to this item can be primary.");
        foreach (var asset in _assets) asset.SetPrimary(asset.AssetId == assetId);
        Touch(now);
        Raise(new CollectibleItemAssetChangedDomainEvent(Guid.NewGuid(), Id, assetId, true, now));
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("Somente itens ativos da coleção podem ser alterados.");
        if (ListedById.HasValue) throw new ConflictException("O item está vinculado a um anúncio. Cancele o anúncio antes de alterar o item.");
    }

    public void ReserveForListing(Guid listingId, DateTimeOffset now)
    {
        if (listingId == Guid.Empty) throw new DomainException("Listing identifier is required.");
        if (!IsActive || ListedById.HasValue && ListedById != listingId)
            throw new ConflictException("Esta unidade não está disponível para o anúncio.");
        if (ListedById == listingId) return;
        ListedById = listingId;
        Touch(now);
        Raise(new CollectibleItemUpdatedDomainEvent(Guid.NewGuid(), Id, CollectionEntryId, now));
    }

    public void ReleaseListing(Guid listingId, DateTimeOffset now)
    {
        if (!IsActive || ListedById != listingId) return;
        ListedById = null;
        Touch(now);
        Raise(new CollectibleItemUpdatedDomainEvent(Guid.NewGuid(), Id, CollectionEntryId, now));
    }

    public void RecordSale(Guid listingId, Guid orderId, Guid buyerItemId, DateTimeOffset now)
    {
        if (!IsActive || ListedById != listingId || orderId == Guid.Empty || buyerItemId == Guid.Empty)
            throw new ConflictException("A transferência não corresponde à unidade anunciada.");
        // Keep the seller's acquisition, notes and private asset links as history.
        // A new ownership record is created for the buyer in the same transaction.
        Status = CollectionRules.SoldStatus;
        Touch(now);
        Raise(new CollectibleItemSoldDomainEvent(Guid.NewGuid(), Id, orderId, buyerItemId, now));
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (Version != expectedVersion) throw new ConflictException("Collectible item changed. Reload and retry.");
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}

public sealed class CollectibleItemAsset
{
    private CollectibleItemAsset() { }
    internal CollectibleItemAsset(Guid itemId, Guid assetId, string type, int sortOrder, bool isPrimary, DateTimeOffset createdAt)
    {
        CollectibleItemId = itemId; AssetId = assetId; Type = type; SortOrder = sortOrder; IsPrimary = isPrimary; CreatedAt = createdAt;
    }
    public Guid CollectibleItemId { get; private set; }
    public Guid AssetId { get; private set; }
    public string Type { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public bool IsPrimary { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    internal void SetPrimary(bool value) => IsPrimary = value;
}
