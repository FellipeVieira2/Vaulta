using Microsoft.EntityFrameworkCore;
using Vaulta.SharedKernel;
using Vaulta.Wallets.Domain;

namespace Vaulta.Wallets.Infrastructure;

public sealed class WalletsDbContext(DbContextOptions<WalletsDbContext> options) : DbContext(options)
{
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletLedgerEntry> WalletLedgerEntries => Set<WalletLedgerEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("wallets");

        modelBuilder.Entity<Wallet>(b =>
        {
            b.ToTable("wallets");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.UserId).IsRequired();
            b.Property(x => x.Balance).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            b.Property(x => x.Version).IsConcurrencyToken();
            b.Ignore(x => x.DomainEvents);
            b.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("ux_wallets_user");
        });

        modelBuilder.Entity<WalletLedgerEntry>(b =>
        {
            b.ToTable("wallet_ledger_entries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.WalletId).IsRequired();
            b.Property(x => x.Type).HasMaxLength(20).IsRequired();
            b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.ReferenceId).HasMaxLength(200);
            b.Property(x => x.Description).HasMaxLength(500);
            b.HasIndex(x => new { x.WalletId, x.CreatedAt }).HasDatabaseName("ix_wallets_ledger_wallet_created");
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages", "identity", table => table.ExcludeFromMigrations());
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Type).HasMaxLength(150).IsRequired();
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Error).HasColumnType("text");
        });

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(Snake(property.Name));
    }

    private static string Snake(string value) => string.Concat(value.Select((c, i) =>
        char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var wallets = ChangeTracker.Entries<Wallet>()
            .Select(x => x.Entity).Where(x => x.DomainEvents.Count > 0).ToArray();

        var events = wallets.SelectMany(x => x.DomainEvents).ToArray();
        foreach (var domainEvent in events)
        {
            OutboxMessages.Add(new OutboxMessage
            {
                Id = domainEvent.Id,
                Type = EventType(domainEvent),
                Payload = System.Text.Json.JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                OccurredAt = domainEvent.OccurredAt
            });
        }

        try
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            foreach (var aggregate in wallets) aggregate.ClearDomainEvents();
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Wallet record changed. Reload and retry.");
        }
    }

    private static string EventType(IDomainEvent domainEvent) => domainEvent switch
    {
        WalletCreditedDomainEvent => "wallets.wallet-credited.v1",
        WalletDebitedDomainEvent => "wallets.wallet-debited.v1",
        WithdrawalRequestedDomainEvent => "wallets.withdrawal-requested.v1",
        _ => throw new InvalidOperationException("Unmapped wallets domain event.")
    };
}