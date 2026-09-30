using Vaulta.Shipping.Contracts;

namespace Vaulta.Shipping.Application.Commands;

public sealed record CreateShipmentCommand(Guid SellerId, CreateShipmentRequest Request);
public sealed record UpdateTrackingCommand(Guid SellerId, Guid ShipmentId, UpdateTrackingRequest Request);