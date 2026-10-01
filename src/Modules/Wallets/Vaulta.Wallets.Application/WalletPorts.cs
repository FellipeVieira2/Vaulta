using Vaulta.Wallets.Domain;

namespace Vaulta.Wallets.Application;

public interface IWalletStore
{
    Task<Wallet?> FindByUserId(Guid userId, CancellationToken cancellationToken);
    Task<Wallet?> GetById(Guid walletId, CancellationToken cancellationToken);
    Task<bool> HasLedgerEntry(Guid walletId, string referenceId, string type, CancellationToken cancellationToken);
    void Add(Wallet wallet);
    Task Save(CancellationToken cancellationToken);
}

public interface IWalletLedgerQueries
{
    Task<Contracts.WalletPageDto> GetTransactions(Guid userId, int page, int pageSize, CancellationToken cancellationToken);
}