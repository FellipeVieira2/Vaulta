using Vaulta.Orders.Contracts;

namespace Vaulta.Orders.Application.Commands;

public sealed record CreateOrderCommand(Guid BuyerId, CreateOrderRequest Request);
public sealed record ConfirmPaymentCommand(Guid BuyerId, Guid OrderId, string PaymentId);
public sealed record MarkShippedCommand(Guid SellerId, Guid OrderId, string TrackingCode);
public sealed record MarkDeliveredCommand(Guid BuyerId, Guid OrderId);
public sealed record CancelOrderCommand(Guid UserId, Guid OrderId, string Reason);