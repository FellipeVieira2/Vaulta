using Vaulta.Payments.Contracts;

namespace Vaulta.Payments.Application.Commands;

public sealed record InitiatePaymentCommand(Guid UserId, CreatePaymentRequest Request);
public sealed record ProcessWebhookCommand(string EventType, string PaymentId, string? Payload);