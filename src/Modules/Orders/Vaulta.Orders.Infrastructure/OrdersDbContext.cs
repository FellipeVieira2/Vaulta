using Microsoft.EntityFrameworkCore;
using Vaulta.Orders.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Orders.Infrastructure;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("orders");

        modelBuilder.Entity<Reservation>(b =>
        {
            b.ToTable("reservations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ListingId).IsRequired();
            b.Property(x => x.BuyerId).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.ReservedUntil).IsRequired();
            b.Ignore(x => x.DomainEvents);
            b.HasIndex(x => new { x.ListingId, x.Status }).HasDatabaseName("ix_orders_reservations_listing_status");
            b.HasIndex(x => new { x.BuyerId, x.ListingId, x.Status }).HasDatabaseName("ix_orders_reservations_buyer_listing_status");
            b.HasIndex(x => x.ReservedUntil).HasFilter("status = 'active'").HasDatabaseName("ix_orders_reservations_expires");
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("orders");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.BuyerId).IsRequired();
            b.Property(x => x.SellerId).IsRequired();
            b.Property(x => x.ListingId).IsRequired();
            b.Property(x => x.CollectibleItemId).IsRequired();
            b.Property(x => x.PrintingId).IsRequired();
            b.Property(x => x.Condition).HasMaxLength(32).IsRequired();
            b.Property(x => x.ItemPriceBrl).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.PlatformFeeBrl).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.TotalAmountBrl).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            b.Property(x => x.ShippingStreet).HasMaxLength(200);
            b.Property(x => x.ShippingCity).HasMaxLength(200);
            b.Property(x => x.ShippingState).HasMaxLength(200);
            b.Property(x => x.ShippingZipCode).HasMaxLength(10);
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.PaymentId).HasMaxLength(200);
            b.Property(x => x.TrackingCode).HasMaxLength(100);
            b.Property(x => x.CancellationReason).HasMaxLength(500);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.Ignore(x => x.DomainEvents);
            b.HasIndex(x => new { x.BuyerId, x.Status }).HasDatabaseName("ix_orders_orders_buyer_status");
            b.HasIndex(x => new { x.SellerId, x.Status }).HasDatabaseName("ix_orders_orders_seller_status");
            b.HasIndex(x => x.ListingId).IsUnique().HasDatabaseName("ux_orders_orders_listing");
            b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_orders_orders_created");
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
        var reservations = ChangeTracker.Entries<Reservation>().Select(x => x.Entity).Where(x => x.DomainEvents.Count > 0).ToArray();
        var orders = ChangeTracker.Entries<Order>().Select(x => x.Entity).Where(x => x.DomainEvents.Count > 0).ToArray();
        var events = reservations.SelectMany(x => x.DomainEvents).Concat(orders.SelectMany(x => x.DomainEvents)).ToArray();

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
            foreach (var aggregate in reservations) aggregate.ClearDomainEvents();
            foreach (var aggregate in orders) aggregate.ClearDomainEvents();
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Record changed. Reload and retry.");
        }
    }

    private static string EventType(IDomainEvent domainEvent) => domainEvent switch
    {
        ReservationCreatedDomainEvent => "orders.reservation-created.v1",
        ReservationExpiredDomainEvent => "orders.reservation-expired.v1",
        ReservationConsumedDomainEvent => "orders.reservation-consumed.v1",
        ReservationReleasedDomainEvent => "orders.reservation-released.v1",
        OrderCreatedDomainEvent => "orders.order-created.v1",
        OrderPaidDomainEvent => "orders.order-paid.v1",
        OrderShippedDomainEvent => "orders.order-shipped.v1",
        OrderDeliveredDomainEvent => "orders.order-delivered.v1",
        OrderCancelledDomainEvent => "orders.order-cancelled.v1",
        _ => throw new InvalidOperationException("Unmapped orders domain event.")
    };
}