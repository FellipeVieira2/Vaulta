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
            ?? CreateWallet(command.UserId);

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

            // Outbox delivery is at-least-once, so a redelivered event must not credit twice.
            var reference = paymentId.ToString();
            if (await store.HasLedgerEntry(wallet.Id, reference, "CREDIT", cancellationToken))
                continue;

            wallet.Credit(Math.Round(credit.Amount, 2), reference, credit.Description ?? "Payment split", occurredAt);
        }

        await store.Save(cancellationToken);
    }

    private Wallet CreateWallet(Guid userId)
    {
        var wallet = Wallet.Create(userId, clock.UtcNow);
        store.Add(wallet);
        return wallet;
    }

    internal static WalletDto MapWallet(Wallet w) => new(w.Id, w.UserId, w.Balance, w.Currency, w.UpdatedAt);
}