using Microsoft.EntityFrameworkCore;
using Vaulta.Wallets.Application;
using Vaulta.Wallets.Contracts;

namespace Vaulta.Wallets.Infrastructure;

public sealed class WalletLedgerQueries(WalletsDbContext db) : IWalletLedgerQueries
{
    public async Task<WalletPageDto> GetTransactions(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var wallet = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);
        if (wallet is null)
            return new WalletPageDto([], page, pageSize, 0);

        var query = db.WalletLedgerEntries.AsNoTracking()
            .Where(e => e.WalletId == wallet.Id)
            .OrderByDescending(e => e.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new WalletTransactionDto(e.Id, e.Type, e.Amount, e.ReferenceId, e.Description, e.CreatedAt))
            .ToListAsync(cancellationToken);

        return new WalletPageDto(items, page, pageSize, totalCount);
    }
}