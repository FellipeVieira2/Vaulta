using Microsoft.EntityFrameworkCore;
using Vaulta.Payments.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Infrastructure;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentSplit> PaymentSplits => Set<PaymentSplit>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");

        modelBuilder.Entity<PaymentTransaction>(b =>
        {
            b.ToTable("payment_transactions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.OrderId).IsRequired();
            b.Property(x => x.BuyerId).IsRequired();
            b.Property(x => x.SellerId).IsRequired();
            b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.NetAmount).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            b.Property(x => x.BillingType).HasMaxLength(20).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.AsaasPaymentId).HasMaxLength(200);
            b.Property(x => x.AsaasCustomerId).HasMaxLength(200);
            b.Property(x => x.CheckoutUrl).HasMaxLength(2000);
            b.Property(x => x.PixQrCode).HasColumnType("text");
            b.Property(x => x.BankSlipUrl).HasMaxLength(2000);
            b.Property(x => x.FailureReason).HasMaxLength(1000);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.Ignore(x => x.DomainEvents);
            b.HasMany(x => x.Splits).WithOne().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Splits).HasField("_splits").UsePropertyAccessMode(PropertyAccessMode.Field);
            b.HasIndex(x => x.OrderId).IsUnique().HasDatabaseName("ux_payments_transaction_order");
            b.HasIndex(x => x.AsaasPaymentId).IsUnique().HasFilter("asaas_payment_id IS NOT NULL").HasDatabaseName("ux_payments_transaction_asaas");
            b.HasIndex(x => new { x.BuyerId, x.Status }).HasDatabaseName("ix_payments_transaction_buyer_status");
            b.HasIndex(x => new { x.SellerId, x.Status }).HasDatabaseName("ix_payments_transaction_seller_status");
        });

        modelBuilder.Entity<PaymentSplit>(b =>
        {
            b.ToTable("payment_splits");
            b.HasKey(x => new { x.PaymentId, x.WalletId });
            b.Property(x => x.WalletId).HasMaxLength(200).IsRequired();
            b.Property(x => x.FixedValue).HasPrecision(18, 2);
            b.Property(x => x.PercentualValue).HasPrecision(5, 2);
            b.Property(x => x.ExternalReference).HasMaxLength(500);
            b.Property(x => x.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<WebhookEvent>(b =>
        {
            b.ToTable("webhook_events");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            b.Property(x => x.AsaasPaymentId).HasMaxLength(200).IsRequired();
            b.Property(x => x.Payload).HasColumnType("jsonb");
            b.Property(x => x.Error).HasColumnType("text");
            b.HasIndex(x => new { x.AsaasPaymentId, x.EventType }).HasDatabaseName("ix_payments_webhook_payment_event");
            b.HasIndex(x => new { x.Processed, x.ReceivedAt }).HasDatabaseName("ix_payments_webhook_pending");
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
        var transactions = ChangeTracker.Entries<PaymentTransaction>()
            .Select(x => x.Entity).Where(x => x.DomainEvents.Count > 0).ToArray();

        var events = transactions.SelectMany(x => x.DomainEvents).ToArray();
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
            foreach (var aggregate in transactions) aggregate.ClearDomainEvents();
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Payment record changed. Reload and retry.");
        }
    }

    private static string EventType(IDomainEvent domainEvent) => domainEvent switch
    {
        PaymentCreatedDomainEvent => "payments.payment-created.v1",
        PaymentConfirmedDomainEvent => "payments.payment-confirmed.v1",
        PaymentFailedDomainEvent => "payments.payment-failed.v1",
        PaymentRefundedDomainEvent => "payments.payment-refunded.v1",
        _ => throw new InvalidOperationException("Unmapped payments domain event.")
    };
}