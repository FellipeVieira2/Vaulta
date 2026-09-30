using Microsoft.EntityFrameworkCore;
using Vaulta.Payments.Application;
using Vaulta.Payments.Domain;

namespace Vaulta.Payments.Infrastructure;

public sealed class PaymentStore(PaymentsDbContext db) : IPaymentStore
{
    public Task<PaymentTransaction?> FindByOrderId(Guid orderId, CancellationToken cancellationToken) =>
        db.PaymentTransactions.Include(x => x.Splits).FirstOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

    public Task<PaymentTransaction?> FindByAsaasId(string asaasPaymentId, CancellationToken cancellationToken) =>
        db.PaymentTransactions.Include(x => x.Splits).FirstOrDefaultAsync(x => x.AsaasPaymentId == asaasPaymentId, cancellationToken);

    public void Add(PaymentTransaction transaction) => db.PaymentTransactions.Add(transaction);

    public void AddWebhookEvent(WebhookEvent webhookEvent) => db.WebhookEvents.Add(webhookEvent);

    public async Task<bool> HasProcessedWebhook(string asaasPaymentId, string eventType, CancellationToken cancellationToken) =>
        await db.WebhookEvents.AnyAsync(x => x.AsaasPaymentId == asaasPaymentId && x.EventType == eventType && x.Processed, cancellationToken);

    public Task Save(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}