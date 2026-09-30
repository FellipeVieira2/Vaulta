using Vaulta.SharedKernel;

namespace Vaulta.Orders.Domain;

public sealed record ReservationCreatedDomainEvent(Guid Id, Guid ReservationId, Guid ListingId, Guid BuyerId, DateTimeOffset ExpiresAt, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record ReservationExpiredDomainEvent(Guid Id, Guid ReservationId, Guid ListingId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record ReservationConsumedDomainEvent(Guid Id, Guid ReservationId, Guid ListingId, Guid OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record ReservationReleasedDomainEvent(Guid Id, Guid ReservationId, Guid ListingId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Reservation : AggregateRoot
{
    private Reservation() { }

    public Guid Id { get; private set; }
    public Guid ListingId { get; private set; }
    public Guid BuyerId { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTimeOffset ReservedUntil { get; private set; }
    public Guid? ConsumedByOrderId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status == OrderRules.ReservationActiveStatus && ReservedUntil > DateTimeOffset.UtcNow;

    public static Reservation Create(Guid listingId, Guid buyerId, DateTimeOffset now)
    {
        if (listingId == Guid.Empty || buyerId == Guid.Empty)
            throw new DomainException("Listing and buyer identifiers are required.");

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            ListingId = listingId,
            BuyerId = buyerId,
            Status = OrderRules.ReservationActiveStatus,
            ReservedUntil = now.Add(OrderRules.ReservationTtl),
            CreatedAt = now,
            UpdatedAt = now
        };
        reservation.Raise(new ReservationCreatedDomainEvent(Guid.NewGuid(), reservation.Id, listingId, buyerId, reservation.ReservedUntil, now));
        return reservation;
    }

    public void Consume(Guid orderId, DateTimeOffset now)
    {
        if (!IsActive) throw new DomainException("Only active reservations can be consumed.");
        Status = OrderRules.ReservationConsumedStatus;
        ConsumedByOrderId = orderId;
        UpdatedAt = now;
        Raise(new ReservationConsumedDomainEvent(Guid.NewGuid(), Id, ListingId, orderId, now));
    }

    public void Release(DateTimeOffset now)
    {
        if (Status != OrderRules.ReservationActiveStatus) return;
        Status = OrderRules.ReservationReleasedStatus;
        UpdatedAt = now;
        Raise(new ReservationReleasedDomainEvent(Guid.NewGuid(), Id, ListingId, now));
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status != OrderRules.ReservationActiveStatus) return;
        Status = OrderRules.ReservationExpiredStatus;
        UpdatedAt = now;
        Raise(new ReservationExpiredDomainEvent(Guid.NewGuid(), Id, ListingId, now));
    }
}