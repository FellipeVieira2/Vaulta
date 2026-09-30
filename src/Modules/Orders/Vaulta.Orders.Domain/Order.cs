using Vaulta.SharedKernel;

namespace Vaulta.Orders.Domain;

public sealed record OrderCreatedDomainEvent(Guid Id, Guid OrderId, Guid BuyerId, Guid SellerId, Guid ListingId, decimal TotalAmount, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderPaidDomainEvent(Guid Id, Guid OrderId, string PaymentId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderShippedDomainEvent(Guid Id, Guid OrderId, string TrackingCode, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderDeliveredDomainEvent(Guid Id, Guid OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderCancelledDomainEvent(Guid Id, Guid OrderId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Order : AggregateRoot
{
    private Order() { }

    public Guid Id { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid ListingId { get; private set; }
    public Guid CollectibleItemId { get; private set; }
    public Guid PrintingId { get; private set; }
    public Guid? VariantId { get; private set; }

    // Snapshot fields captured at order creation time
    public string Condition { get; private set; } = null!;
    public decimal ItemPriceBrl { get; private set; }
    public decimal PlatformFeeBrl { get; private set; }
    public decimal TotalAmountBrl { get; private set; }
    public string Currency { get; private set; } = "BRL";

    // Shipping address snapshot
    public string? ShippingStreet { get; private set; }
    public string? ShippingCity { get; private set; }
    public string? ShippingState { get; private set; }
    public string? ShippingZipCode { get; private set; }

    public string Status { get; private set; } = null!;
    public string? PaymentId { get; private set; }
    public string? TrackingCode { get; private set; }
    public string? CancellationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid Version { get; private set; }

    public static Order Create(
        Guid buyerId,
        Guid sellerId,
        Guid listingId,
        Guid collectibleItemId,
        Guid printingId,
        Guid? variantId,
        string condition,
        decimal itemPriceBrl,
        decimal platformFeeBrl,
        string? shippingStreet,
        string? shippingCity,
        string? shippingState,
        string? shippingZipCode,
        DateTimeOffset now)
    {
        if (buyerId == Guid.Empty || sellerId == Guid.Empty || listingId == Guid.Empty)
            throw new DomainException("Buyer, seller and listing identifiers are required.");
        if (buyerId == sellerId)
            throw new DomainException("Buyer and seller cannot be the same user.");

        var normalizedCondition = OrderRules.ValidateCondition(condition);
        var normalizedPrice = OrderRules.ValidatePrice(itemPriceBrl);
        var normalizedFee = Math.Round(platformFeeBrl, 2);
        var total = Math.Round(normalizedPrice + normalizedFee, 2);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            ListingId = listingId,
            CollectibleItemId = collectibleItemId,
            PrintingId = printingId,
            VariantId = variantId,
            Condition = normalizedCondition,
            ItemPriceBrl = normalizedPrice,
            PlatformFeeBrl = normalizedFee,
            TotalAmountBrl = total,
            ShippingStreet = OrderRules.ValidateAddress(shippingStreet, "Shipping street"),
            ShippingCity = OrderRules.ValidateAddress(shippingCity, "Shipping city"),
            ShippingState = OrderRules.ValidateAddress(shippingState, "Shipping state"),
            ShippingZipCode = OrderRules.ValidateZipCode(shippingZipCode),
            Status = OrderRules.PendingStatus,
            CreatedAt = now,
            UpdatedAt = now,
            Version = Guid.NewGuid()
        };
        order.Raise(new OrderCreatedDomainEvent(Guid.NewGuid(), order.Id, buyerId, sellerId, listingId, total, now));
        return order;
    }

    public void MarkAsPaid(string paymentId, DateTimeOffset now)
    {
        if (Status != OrderRules.PendingStatus)
            throw new DomainException($"Order cannot be marked as paid from status '{Status}'.");
        if (string.IsNullOrWhiteSpace(paymentId))
            throw new DomainException("Payment identifier is required.");

        Status = OrderRules.PaidStatus;
        PaymentId = paymentId;
        PaidAt = now;
        Touch(now);
        Raise(new OrderPaidDomainEvent(Guid.NewGuid(), Id, paymentId, now));
    }

    public void MarkAsShipped(string trackingCode, DateTimeOffset now)
    {
        if (Status != OrderRules.PaidStatus)
            throw new DomainException($"Order cannot be shipped from status '{Status}'.");
        if (string.IsNullOrWhiteSpace(trackingCode))
            throw new DomainException("Tracking code is required.");

        Status = OrderRules.ShippedStatus;
        TrackingCode = trackingCode;
        ShippedAt = now;
        Touch(now);
        Raise(new OrderShippedDomainEvent(Guid.NewGuid(), Id, trackingCode, now));
    }

    public void MarkAsDelivered(DateTimeOffset now)
    {
        if (Status != OrderRules.ShippedStatus)
            throw new DomainException($"Order cannot be delivered from status '{Status}'.");

        Status = OrderRules.DeliveredStatus;
        DeliveredAt = now;
        Touch(now);
        Raise(new OrderDeliveredDomainEvent(Guid.NewGuid(), Id, now));
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        if (Status is OrderRules.DeliveredStatus or OrderRules.RefundedStatus)
            throw new DomainException($"Order cannot be cancelled from status '{Status}'.");
        if (Status == OrderRules.CancelledStatus)
            throw new DomainException("Order is already cancelled.");

        Status = OrderRules.CancelledStatus;
        CancellationReason = reason;
        CancelledAt = now;
        Touch(now);
        Raise(new OrderCancelledDomainEvent(Guid.NewGuid(), Id, reason, now));
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}