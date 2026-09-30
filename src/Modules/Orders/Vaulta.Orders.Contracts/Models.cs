namespace Vaulta.Orders.Contracts;

public sealed record CreateOrderRequest(Guid ListingId, string? ShippingStreet, string? ShippingCity, string? ShippingState, string? ShippingZipCode);
public sealed record ConfirmPaymentRequest(string PaymentId);
public sealed record MarkShippedRequest(string TrackingCode);
public sealed record CancelOrderRequest(string Reason);

public sealed record ReservationDto(Guid Id, Guid ListingId, string Status, DateTimeOffset ReservedUntil, DateTimeOffset CreatedAt);
public sealed record OrderSnapshotDto(string Condition, decimal ItemPriceBrl, decimal PlatformFeeBrl, decimal TotalAmountBrl, string Currency);
public sealed record OrderShippingDto(string? Street, string? City, string? State, string? ZipCode);
public sealed record OrderDto(
    Guid Id,
    Guid BuyerId,
    Guid SellerId,
    Guid ListingId,
    Guid CollectibleItemId,
    Guid PrintingId,
    Guid? VariantId,
    OrderSnapshotDto Snapshot,
    OrderShippingDto Shipping,
    string Status,
    string? PaymentId,
    string? TrackingCode,
    string? CancellationReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    Guid Version);
public sealed record OrderPageDto(IReadOnlyList<OrderDto> Items, int Page, int PageSize, int TotalCount);