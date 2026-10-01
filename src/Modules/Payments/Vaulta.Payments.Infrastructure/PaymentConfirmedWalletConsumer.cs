using System.Text.Json;
using Vaulta.Identity.Application;
using Vaulta.Payments.Application;
using Vaulta.Wallets.Application;

namespace Vaulta.Payments.Infrastructure;

public sealed class PaymentConfirmedWalletConsumer(IPaymentStore payments, WalletCommandHandlers wallets) : IEventConsumer
{
    public async Task Handle(EventEnvelope message, CancellationToken ct)
    {
        if (message.Type != "payments.payment-confirmed.v1") return;

        using var doc = JsonDocument.Parse(message.Payload);
        if (!doc.RootElement.TryGetProperty("PaymentId", out var idProp) || !idProp.TryGetGuid(out var paymentId))
            return;

        var payment = await payments.FindById(paymentId, ct);
        if (payment is null) return;

        var credits = payment.Splits
            .Select(s => new WalletCreditSplit(
                s.WalletId,
                s.FixedValue ?? Math.Round(payment.Amount * (s.PercentualValue ?? 0m) / 100m, 2),
                s.Description))
            .ToArray();

        await wallets.CreditFromPaymentConfirmed(payment.Id, payment.Amount, credits, message.OccurredAt, ct);
    }
}
