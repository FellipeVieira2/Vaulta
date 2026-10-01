using Microsoft.EntityFrameworkCore;
using Vaulta.Payments.Application;
using Vaulta.Payments.Domain;

namespace Vaulta.Payments.Infrastructure;

public sealed class RefundStore(PaymentsDbContext db) : IRefundStore
{
    public async Task<IReadOnlyList<Guid>> Due(DateTimeOffset now, CancellationToken ct) =>
        await db.PaymentTransactions.AsNoTracking()
            .Where(x => x.RefundRequestedAt != null && x.RefundStatus != "DONE" && x.RefundNextCheckAt <= now)
            .OrderBy(x => x.RefundNextCheckAt).ThenBy(x => x.Id).Select(x => x.Id).Take(50).ToArrayAsync(ct);

    public async Task<bool> Claim(PaymentTransaction payment, DateTimeOffset now, CancellationToken ct)
    {
        var version = Guid.NewGuid();
        var count = await db.PaymentTransactions.Where(x => x.Id == payment.Id && x.Version == payment.Version
                && x.RefundStatus == "REQUESTED" && x.RefundSubmittedAt == null
                && x.Status == PaymentRules.ConfirmedStatus && x.PayoutHoldReason == "REFUND_REQUESTED"
                && x.Currency == "BRL" && x.AsaasPaymentId != null && !x.Splits.Any())
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RefundStatus, "SUBMITTING")
                .SetProperty(x => x.RefundSubmittedAt, now).SetProperty(x => x.RefundNextCheckAt, now.AddMinutes(1))
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.Version, version), ct);
        if (count != 1) return false;
        payment.ClaimRefund(now, version);
        db.Entry(payment).OriginalValues.SetValues(payment);
        db.Entry(payment).State = EntityState.Unchanged;
        return true;
    }
}
