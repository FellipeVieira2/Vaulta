using Microsoft.EntityFrameworkCore;
using Vaulta.Payments.Application;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Infrastructure;

public sealed class PayoutStore(PaymentsDbContext db) : IPayoutStore
{
    public Task<SellerPixDestination?> FindDestination(Guid sellerId, CancellationToken ct) =>
        db.SellerPixDestinations.SingleOrDefaultAsync(x => x.SellerId == sellerId, ct);
    public void Add(SellerPixDestination destination) => db.SellerPixDestinations.Add(destination);
    public void Add(SellerPayout payout) => db.SellerPayouts.Add(payout);
    public void Add(TransferWebhookReceipt receipt) => db.TransferWebhookReceipts.Add(receipt);
    public Task<SellerPayout?> Find(Guid id, CancellationToken ct) => db.SellerPayouts.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<SellerPayout?> FindByReference(string reference, CancellationToken ct) =>
        db.SellerPayouts.SingleOrDefaultAsync(x => x.ExternalReference == reference, ct);
    public Task<bool> HasWebhook(string eventId, CancellationToken ct) => db.TransferWebhookReceipts.AnyAsync(x => x.Id == eventId, ct);
    public async Task<IReadOnlyList<Guid>> UnrecordedPayments(CancellationToken ct) =>
        await db.PaymentTransactions.AsNoTracking()
            .Where(x => x.Status == PaymentRules.ConfirmedStatus && !db.SellerPayouts.Any(p => p.PaymentId == x.Id))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => x.Id).Take(50).ToArrayAsync(ct);
    public async Task<IReadOnlyList<Guid>> Due(DateTimeOffset now, CancellationToken ct) =>
        await db.SellerPayouts.AsNoTracking()
            .Where(x => x.Status != PayoutStatus.Done && x.Status != PayoutStatus.Failed && x.NextCheckAt <= now)
            .OrderBy(x => x.NextCheckAt).ThenBy(x => x.Id).Select(x => x.Id).Take(50).ToArrayAsync(ct);

    public async Task<bool> Claim(SellerPayout payout, DateTimeOffset now, CancellationToken ct)
    {
        var version = Guid.NewGuid();
        var count = await db.SellerPayouts.Where(x => x.Id == payout.Id && x.Version == payout.Version
                && x.Status == PayoutStatus.Ready && x.SubmittedAt == null
                && db.PaymentTransactions.Any(t => t.Id == x.PaymentId && t.SellerId == x.SellerId
                    && t.Status == PaymentRules.ConfirmedStatus && t.ReceivedAt != null && t.PayoutHoldReason == null
                    && t.Currency == "BRL" && t.Amount == x.ItemPriceBrl && t.NetAmount == x.AmountBrl + x.PlatformFeeBrl
                    && !t.Splits.Any())
                && db.SellerPixDestinations.Any(d => d.SellerId == x.SellerId && d.IsVerified && d.Version == x.DestinationVersion))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, PayoutStatus.Submitting)
                .SetProperty(x => x.SubmittedAt, now).SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.NextCheckAt, now.AddMinutes(2)).SetProperty(x => x.Version, version), ct);
        if (count != 1) return false;
        payout.MarkClaimed(now, version);
        // ExecuteUpdate bypasses tracking. Explicitly replace the original
        // concurrency snapshot before saving the provider response.
        db.Entry(payout).OriginalValues.SetValues(payout);
        db.Entry(payout).State = EntityState.Unchanged;
        return true;
    }

    public async Task<SellerPayoutPageDto> ListMine(Guid sellerId, int page, int pageSize, CancellationToken ct)
    {
        var pagination = Pagination.Normalize(page, pageSize);
        var query = db.SellerPayouts.AsNoTracking().Where(x => x.SellerId == sellerId);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip(pagination.Offset).Take(pagination.Size)
            .Select(x => new SellerPayoutDto(x.Id, x.OrderId, x.ItemPriceBrl, x.PlatformFeeBrl,
                x.AmountBrl, x.Currency, x.Status, x.CreatedAt, x.CompletedAt,
                x.PixKey == null ? null : "••••" + x.PixKey.Substring(x.PixKey.Length >= 4 ? x.PixKey.Length - 4 : 0),
                x.PaymentFeeBrl, x.TransferFeeBrl, x.SellerNetBrl)).ToArrayAsync(ct);
        return new(items, pagination.Page, pagination.Size, count);
    }
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
