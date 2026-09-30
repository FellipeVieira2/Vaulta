using Vaulta.Wallets.Contracts;

namespace Vaulta.Wallets.Application.Commands;

public sealed record CreditWalletCommand(Guid UserId, decimal Amount, string? ReferenceId, string? Description);
public sealed record RequestWithdrawalCommand(Guid UserId, WithdrawRequest Request);