using Vaulta.SharedKernel;

namespace Vaulta.Wallets.Domain;

public sealed record WalletCreditedDomainEvent(Guid Id, Guid WalletId, decimal Amount, string? ReferenceId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record WalletDebitedDomainEvent(Guid Id, Guid WalletId, decimal Amount, string? ReferenceId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record WithdrawalRequestedDomainEvent(Guid Id, Guid WalletId, decimal Amount, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Wallet : AggregateRoot
{
    private readonly List<WalletLedgerEntry> _entries = [];

    private Wallet() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public decimal Balance { get; private set; }
    public string Currency { get; private set; } = "BRL";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid Version { get; private set; }
    public IReadOnlyCollection<WalletLedgerEntry> Entries => _entries.AsReadOnly();

    public static Wallet Create(Guid userId, DateTimeOffset now)
    {
        if (userId == Guid.Empty) throw new DomainException("User identifier is required.");
        return new Wallet
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Balance = 0m,
            Currency = "BRL",
            CreatedAt = now,
            UpdatedAt = now,
            Version = Guid.NewGuid()
        };
    }

    public void Credit(decimal amount, string? referenceId, string? description, DateTimeOffset now)
    {
        if (amount <= 0) throw new DomainException("Credit amount must be greater than zero.");
        Balance = Math.Round(Balance + amount, 2);
        var entry = new WalletLedgerEntry(Id, "CREDIT", Math.Round(amount, 2), referenceId, description, now);
        _entries.Add(entry);
        Touch(now);
        Raise(new WalletCreditedDomainEvent(Guid.NewGuid(), Id, amount, referenceId, now));
    }

    public void Debit(decimal amount, string? referenceId, string? description, DateTimeOffset now)
    {
        if (amount <= 0) throw new DomainException("Debit amount must be greater than zero.");
        if (Balance < amount) throw new DomainException($"Insufficient balance. Available: {Balance:N2}, Requested: {amount:N2}.");
        Balance = Math.Round(Balance - amount, 2);
        var entry = new WalletLedgerEntry(Id, "DEBIT", Math.Round(-amount, 2), referenceId, description, now);
        _entries.Add(entry);
        Touch(now);
        Raise(new WalletDebitedDomainEvent(Guid.NewGuid(), Id, amount, referenceId, now));
    }

    public void RequestWithdrawal(decimal amount, DateTimeOffset now)
    {
        if (amount <= 0) throw new DomainException("Withdrawal amount must be greater than zero.");
        if (Balance < amount) throw new DomainException($"Insufficient balance for withdrawal. Available: {Balance:N2}, Requested: {amount:N2}.");
        Debit(amount, null, "Withdrawal request", now);
        Raise(new WithdrawalRequestedDomainEvent(Guid.NewGuid(), Id, amount, now));
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}

public sealed class WalletLedgerEntry
{
    private WalletLedgerEntry() { }

    internal WalletLedgerEntry(Guid walletId, string type, decimal amount, string? referenceId, string? description, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        WalletId = walletId;
        Type = type;
        Amount = amount;
        ReferenceId = referenceId;
        Description = description;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public string Type { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string? ReferenceId { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}