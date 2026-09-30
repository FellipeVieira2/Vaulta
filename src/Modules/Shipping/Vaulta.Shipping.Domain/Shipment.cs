using Vaulta.SharedKernel;

namespace Vaulta.Shipping.Domain;

public sealed record ShipmentCreatedDomainEvent(Guid Id, Guid ShipmentId, Guid OrderId, string Carrier, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record ShipmentTrackingUpdatedDomainEvent(Guid Id, Guid ShipmentId, string Status, string? TrackingCode, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record ShipmentDeliveredDomainEvent(Guid Id, Guid ShipmentId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Shipment : AggregateRoot
{
    private readonly List<ShipmentEvent> _events = [];
    private Shipment() { }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid BuyerId { get; private set; }
    public string Carrier { get; private set; } = null!;
    public string? TrackingCode { get; private set; }
    public string Status { get; private set; } = null!;
    public decimal? ShippingCostBrl { get; private set; }
    public string? OriginZipCode { get; private set; }
    public string? DestinationZipCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public Guid Version { get; private set; }
    public IReadOnlyCollection<ShipmentEvent> Events => _events.AsReadOnly();

    public static Shipment Create(
        Guid orderId,
        Guid sellerId,
        Guid buyerId,
        string carrier,
        string? trackingCode,
        decimal? shippingCostBrl,
        string? originZipCode,
        string? destinationZipCode,
        DateTimeOffset now)
    {
        if (orderId == Guid.Empty || sellerId == Guid.Empty || buyerId == Guid.Empty)
            throw new DomainException("Order, seller and buyer identifiers are required.");
        if (string.IsNullOrWhiteSpace(carrier))
            throw new DomainException("Carrier is required.");

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            SellerId = sellerId,
            BuyerId = buyerId,
            Carrier = carrier.Trim().ToUpperInvariant(),
            TrackingCode = trackingCode?.Trim(),
            Status = ShippingRules.CreatedStatus,
            ShippingCostBrl = shippingCostBrl.HasValue ? Math.Round(shippingCostBrl.Value, 2) : null,
            OriginZipCode = originZipCode?.Trim(),
            DestinationZipCode = destinationZipCode?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            Version = Guid.NewGuid()
        };
        shipment.Raise(new ShipmentCreatedDomainEvent(Guid.NewGuid(), shipment.Id, orderId, shipment.Carrier, now));
        return shipment;
    }

    public void UpdateTracking(string status, string? trackingCode, string? description, DateTimeOffset occurredAt, DateTimeOffset now)
    {
        if (Status == ShippingRules.DeliveredStatus)
            throw new DomainException("Delivered shipments cannot be updated.");

        var normalizedStatus = ShippingRules.NormalizeStatus(status);
        Status = normalizedStatus;
        if (!string.IsNullOrWhiteSpace(trackingCode))
            TrackingCode = trackingCode.Trim();

        _events.Add(new ShipmentEvent(Id, normalizedStatus, description, trackingCode, occurredAt));
        Touch(now);

        if (normalizedStatus == ShippingRules.DeliveredStatus)
        {
            DeliveredAt = occurredAt;
            Raise(new ShipmentDeliveredDomainEvent(Guid.NewGuid(), Id, occurredAt));
        }
        else
        {
            Raise(new ShipmentTrackingUpdatedDomainEvent(Guid.NewGuid(), Id, normalizedStatus, TrackingCode, now));
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}

public sealed class ShipmentEvent
{
    private ShipmentEvent() { }
    internal ShipmentEvent(Guid shipmentId, string status, string? description, string? trackingCode, DateTimeOffset occurredAt)
    {
        ShipmentId = shipmentId;
        Status = status;
        Description = description;
        TrackingCode = trackingCode;
        OccurredAt = occurredAt;
    }

    public Guid ShipmentId { get; private set; }
    public string Status { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? TrackingCode { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}