using FluentValidation;
using Vaulta.Marketplace.Domain;
using Vaulta.Orders.Application.Commands;
using Vaulta.Orders.Contracts;
using Vaulta.Orders.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Orders.Application;

public sealed class OrderCommandHandlers(
    IOrderStore store,
    IOrderMarketplace marketplace,
    IClock clock)
{
    public async Task<OrderDto> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        await new CreateOrderValidator().ValidateAndThrowAsync(command.Request, cancellationToken);

        var listing = await marketplace.GetActiveListing(command.Request.ListingId, cancellationToken)
            ?? throw new NotFoundException("Listing not found or no longer active.");

        if (listing.SellerUserId == command.BuyerId)
            throw new DomainException("You cannot purchase your own listing.");

        // Check for existing active reservation by this buyer on this listing
        var existingReservation = await store.FindReservationByBuyerAndListing(command.BuyerId, listing.Id, cancellationToken);
        if (existingReservation is { IsActive: true })
            throw new ConflictException("You already have an active reservation for this listing. Complete or cancel it first.");

        var now = clock.UtcNow;

        // Create reservation atomically with order
        var reservation = Reservation.Create(listing.Id, command.BuyerId, now);
        store.AddReservation(reservation);

        var platformFee = MarketplaceRules.CalculatePlatformFee(listing.PriceBrl);
        var order = Order.Create(
            command.BuyerId,
            listing.SellerUserId,
            listing.Id,
            listing.CollectibleItemId,
            listing.PrintingId,
            listing.VariantId,
            listing.Condition,
            listing.PriceBrl,
            platformFee,
            command.Request.ShippingStreet,
            command.Request.ShippingCity,
            command.Request.ShippingState,
            command.Request.ShippingZipCode,
            now);

        store.AddOrder(order);
        reservation.Consume(order.Id, now);

        await store.Save(cancellationToken);

        // Mark listing as sold after successful persistence
        await marketplace.MarkListingAsSold(listing.Id, order.Id, now, cancellationToken);

        return MapOrder(order);
    }

    public async Task Handle(ConfirmPaymentCommand command, CancellationToken cancellationToken)
    {
        var order = await RequireOrder(command.OrderId, cancellationToken);
        order.MarkAsPaid(command.PaymentId, clock.UtcNow);
        await store.Save(cancellationToken);
    }

    public async Task Handle(MarkShippedCommand command, CancellationToken cancellationToken)
    {
        var order = await RequireOrder(command.OrderId, cancellationToken);
        if (order.SellerId != command.SellerId)
            throw new ForbiddenException("Only the seller can mark an order as shipped.");
        order.MarkAsShipped(command.TrackingCode, clock.UtcNow);
        await store.Save(cancellationToken);
    }

    public async Task Handle(MarkDeliveredCommand command, CancellationToken cancellationToken)
    {
        var order = await RequireOrder(command.OrderId, cancellationToken);
        order.MarkAsDelivered(clock.UtcNow);
        await store.Save(cancellationToken);
    }

    public async Task Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await RequireOrder(command.OrderId, cancellationToken);
        if (order.BuyerId != command.UserId && order.SellerId != command.UserId)
            throw new ForbiddenException("Only the buyer or seller can cancel an order.");
        order.Cancel(command.Reason, clock.UtcNow);
        await store.Save(cancellationToken);

        // Release the listing back to active if cancelled before payment
        if (order.Status == OrderRules.CancelledStatus && order.PaidAt is null)
            await marketplace.ReleaseListing(order.ListingId, cancellationToken);
    }

    private async Task<Order> RequireOrder(Guid orderId, CancellationToken cancellationToken) =>
        await store.FindOrder(orderId, cancellationToken)
        ?? throw new NotFoundException("Order not found.");

    internal static OrderDto MapOrder(Order o) => new(
        o.Id, o.BuyerId, o.SellerId, o.ListingId, o.CollectibleItemId, o.PrintingId, o.VariantId,
        new OrderSnapshotDto(o.Condition, o.ItemPriceBrl, o.PlatformFeeBrl, o.TotalAmountBrl, o.Currency),
        new OrderShippingDto(o.ShippingStreet, o.ShippingCity, o.ShippingState, o.ShippingZipCode),
        o.Status, o.PaymentId, o.TrackingCode, o.CancellationReason,
        o.CreatedAt, o.UpdatedAt, o.PaidAt, o.ShippedAt, o.DeliveredAt, o.CancelledAt, o.Version);
}

public sealed class CreateOrderValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
        RuleFor(x => x.ShippingStreet).MaximumLength(200);
        RuleFor(x => x.ShippingCity).MaximumLength(200);
        RuleFor(x => x.ShippingState).MaximumLength(200);
        RuleFor(x => x.ShippingZipCode).MaximumLength(10);
    }
}