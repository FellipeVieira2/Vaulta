using Vaulta.SharedKernel;

namespace Vaulta.Collection.Domain;

public sealed record CollectibleItemsAddedDomainEvent(Guid Id, Guid CollectionEntryId, Guid UserId, IReadOnlyList<Guid> ItemIds, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class CollectionEntry : AggregateRoot
{
    private CollectionEntry() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid PrintingId { get; private set; }
    public Guid? VariantId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CollectionEntry Create(Guid userId, Guid printingId, Guid? variantId, DateTimeOffset now)
    {
        if (userId == Guid.Empty || printingId == Guid.Empty || variantId == Guid.Empty)
            throw new DomainException("User, printing and optional variant identifiers must be valid.");
        return new CollectionEntry { Id = Guid.NewGuid(), UserId = userId, PrintingId = printingId, VariantId = variantId, CreatedAt = now, UpdatedAt = now };
    }

    public IReadOnlyList<CollectibleItem> AddItems(int quantity, string condition, Money? acquisitionPrice, DateOnly? acquisitionDate, string? notes, DateTimeOffset now)
    {
        if (quantity is < 1 or > 100) throw new DomainException("Quantity must be between 1 and 100.");
        CollectionRules.ValidateAcquisitionPrice(acquisitionPrice);
        condition = CollectionRules.Condition(condition);
        notes = CollectionRules.Notes(notes);
        var items = Enumerable.Range(0, quantity).Select(_ => CollectibleItem.Create(Id, UserId, condition, acquisitionPrice, acquisitionDate, notes, now)).ToArray();
        UpdatedAt = now;
        Raise(new CollectibleItemsAddedDomainEvent(Guid.NewGuid(), Id, UserId, items.Select(x => x.Id).ToArray(), now));
        return items;
    }
}
