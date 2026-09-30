namespace Vaulta.Shipping.Contracts;

public sealed record CreateShipmentRequest(Guid OrderId, string Carrier, string? TrackingCode, decimal? ShippingCostBrl, string? OriginZipCode, string? DestinationZipCode);
public sealed record UpdateTrackingRequest(string Status, string? TrackingCode, string? Description, DateTimeOffset OccurredAt);
public sealed record ShipmentEventDto(string Status, string? Description, string? TrackingCode, DateTimeOffset OccurredAt);
public sealed record ShipmentDto(
    Guid Id,
    Guid OrderId,
    Guid SellerId,
    Guid BuyerId,
    string Carrier,
    string? TrackingCode,
    string Status,
    decimal? ShippingCostBrl,
    string? OriginZipCode,
    string? DestinationZipCode,
    IReadOnlyList<ShipmentEventDto> Events,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeliveredAt,
    Guid Version);