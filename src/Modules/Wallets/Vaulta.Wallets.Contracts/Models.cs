namespace Vaulta.Wallets.Contracts;

public sealed record WalletDto(Guid Id, Guid UserId, decimal Balance, string Currency, DateTimeOffset UpdatedAt);
public sealed record WalletTransactionDto(Guid Id, string Type, decimal Amount, string? ReferenceId, string? Description, DateTimeOffset CreatedAt);
public sealed record WalletPageDto(IReadOnlyList<WalletTransactionDto> Items, int Page, int PageSize, int TotalCount);
public sealed record WithdrawRequest(decimal Amount);