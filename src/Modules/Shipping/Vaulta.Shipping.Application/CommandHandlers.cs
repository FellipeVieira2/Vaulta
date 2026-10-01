using FluentValidation;
using Vaulta.SharedKernel;
using Vaulta.Shipping.Application.Commands;
using Vaulta.Shipping.Contracts;
using Vaulta.Shipping.Domain;

namespace Vaulta.Shipping.Application;

public sealed class ShippingCommandHandlers(
    IShippingStore store,
    IShippingOrders orders,
    IClock clock)
{
    public async Task<ShipmentDto> Handle(CreateShipmentCommand command, CancellationToken cancellationToken)
    {
        await new CreateShipmentValidator().ValidateAndThrowAsync(command.Request, cancellationToken);

        var order = await orders.GetOrder(command.Request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order not found.");

        if (order.SellerId != command.SellerId)
            throw new ForbiddenException("Only the seller can create shipments for this order.");

        if (order.Status != Orders.Domain.OrderRules.PaidStatus)
            throw new ConflictException($"Shipments can only be created for paid orders. Current status: {order.Status}");

        var existing = await store.FindByOrderId(order.Id, cancellationToken);
        if (existing is not null && existing.Status != ShippingRules.FailedStatus && existing.Status != ShippingRules.ReturnedStatus)
            throw new ConflictException("A shipment already exists for this order.");

        var now = clock.UtcNow;
        var shipment = Shipment.Create(
            order.Id,
            command.SellerId,
            order.BuyerId,
            command.Request.Carrier,
            command.Request.TrackingCode,
            command.Request.ShippingCostBrl,
            command.Request.OriginZipCode,
            command.Request.DestinationZipCode,
            now);

        store.Add(shipment);
        await store.Save(cancellationToken);

        if (!string.IsNullOrWhiteSpace(command.Request.TrackingCode))
            await orders.MarkOrderAsShipped(order.Id, command.Request.TrackingCode, cancellationToken);

        return MapShipment(shipment);
    }

    public async Task Handle(UpdateTrackingCommand command, CancellationToken cancellationToken)
    {
        await new UpdateTrackingValidator().ValidateAndThrowAsync(command.Request, cancellationToken);

        var shipment = await store.FindById(command.ShipmentId, cancellationToken)
            ?? throw new NotFoundException("Shipment not found.");

        if (shipment.SellerId != command.SellerId)
            throw new ForbiddenException("Only the seller can update tracking for this shipment.");

        shipment.UpdateTracking(
            command.Request.Status,
            command.Request.TrackingCode,
            command.Request.Description,
            command.Request.OccurredAt,
            clock.UtcNow);

        await store.Save(cancellationToken);

        // Carrier/seller tracking does not confirm buyer receipt or authorize seller payout.
    }

    public static ShipmentDto MapShipment(Shipment s) => new(
        s.Id, s.OrderId, s.SellerId, s.BuyerId, s.Carrier, s.TrackingCode, s.Status,
        s.ShippingCostBrl, s.OriginZipCode, s.DestinationZipCode,
        s.Events.Select(e => new ShipmentEventDto(e.Status, e.Description, e.TrackingCode, e.OccurredAt)).ToArray(),
        s.CreatedAt, s.UpdatedAt, s.DeliveredAt, s.Version);
}

public sealed class CreateShipmentValidator : AbstractValidator<CreateShipmentRequest>
{
    public CreateShipmentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Carrier).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TrackingCode).MaximumLength(100);
        RuleFor(x => x.ShippingCostBrl).GreaterThanOrEqualTo(0).When(x => x.ShippingCostBrl.HasValue);
        RuleFor(x => x.OriginZipCode).MaximumLength(10);
        RuleFor(x => x.DestinationZipCode).MaximumLength(10);
    }
}

public sealed class UpdateTrackingValidator : AbstractValidator<UpdateTrackingRequest>
{
    public UpdateTrackingValidator()
    {
        RuleFor(x => x.Status).NotEmpty();
        RuleFor(x => x.TrackingCode).MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.OccurredAt).NotEmpty();
    }
}
