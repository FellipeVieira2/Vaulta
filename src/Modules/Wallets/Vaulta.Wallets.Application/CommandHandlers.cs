using Vaulta.SharedKernel;
using Vaulta.Wallets.Application.Commands;
using Vaulta.Wallets.Contracts;
using Vaulta.Wallets.Domain;

namespace Vaulta.Wallets.Application;

public sealed record WalletCreditSplit(string WalletId, decimal Amount, string? Description);

public sealed class WalletCommandHandlers(
    IWalletStore store,
    IClock clock)
{
    public async Task<WalletDto> Handle(CreditWalletCommand command, CancellationToken cancellationToken)
    {
        if (command.Amount <= 0)
            throw new DomainException("Credit amount must be greater than zero.");

        var wallet = await store.FindByUserId(command.UserId, cancellationToken)
            ?? Wallet.Create(command.UserId, clock.UtcNow);

        if (wallet.Id == Guid.Empty)
            store.Add(wallet);

        wallet.Credit(command.Amount, command.ReferenceId, command.Description, clock.UtcNow);
        await store.Save(cancellationToken);

        return MapWallet(wallet);
    }

    public async Task<WalletDto> Handle(RequestWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var wallet = await store.FindByUserId(command.UserId, cancellationToken)
            ?? throw new NotFoundException("Wallet not found for this user.");

        wallet.RequestWithdrawal(command.Request.Amount, clock.UtcNow);
        await store.Save(cancellationToken);

        return MapWallet(wallet);
    }

    public async Task CreditFromPaymentConfirmed(Guid paymentId, decimal paymentAmount, IReadOnlyList<WalletCreditSplit> credits, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        foreach (var credit in credits)
        {
            if (!Guid.TryParse(credit.WalletId, out var walletId))
                continue;

            var wallet = await store.GetById(walletId, cancellationToken);
            if (wallet is null)
                continue;

            if (credit.Amount <= 0) continue;

            wallet.Credit(Math.Round(credit.Amount, 2), paymentId.ToString(), credit.Description ?? "Payment split", occurredAt);
        }

        await store.Save(cancellationToken);
    }

    internal static WalletDto MapWallet(Wallet w) => new(w.Id, w.UserId, w.Balance, w.Currency, w.UpdatedAt);
}