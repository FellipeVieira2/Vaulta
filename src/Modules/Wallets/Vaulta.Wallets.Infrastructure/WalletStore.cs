using Microsoft.EntityFrameworkCore;
using Vaulta.Wallets.Application;
using Vaulta.Wallets.Domain;

namespace Vaulta.Wallets.Infrastructure;

public sealed class WalletStore(WalletsDbContext db) : IWalletStore
{
    public async Task<Wallet?> FindByUserId(Guid userId, CancellationToken cancellationToken) =>
        await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

    public async Task<Wallet?> GetById(Guid walletId, CancellationToken cancellationToken) =>
        await db.Wallets.FirstOrDefaultAsync(w => w.Id == walletId, cancellationToken);

    public Task<bool> HasLedgerEntry(Guid walletId, string referenceId, string type, CancellationToken cancellationToken) =>
        db.WalletLedgerEntries.AnyAsync(e => e.WalletId == walletId && e.ReferenceId == referenceId && e.Type == type, cancellationToken);

    public void Add(Wallet wallet) => db.Wallets.Add(wallet);

    public async Task Save(CancellationToken cancellationToken) => await db.SaveChangesAsync(cancellationToken);
}