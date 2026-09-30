using Microsoft.EntityFrameworkCore;
using Vaulta.Shipping.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Shipping.Infrastructure;

public sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options) : DbContext(options)
{
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("shipping");

        modelBuilder.Entity<Shipment>(b =>
        {
            b.ToTable("shipments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.OrderId).IsRequired();
            b.Property(x => x.SellerId).IsRequired();
            b.Property(x => x.BuyerId).IsRequired();
            b.Property(x => x.Carrier).HasMaxLength(100).IsRequired();
            b.Property(x => x.TrackingCode).HasMaxLength(100);
            b.Property(x => x.Status).HasMaxLength(30).IsRequired();
            b.Property(x => x.ShippingCostBrl).HasPrecision(18, 2);
            b.Property(x => x.OriginZipCode).HasMaxLength(10);
            b.Property(x => x.DestinationZipCode).HasMaxLength(10);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.Ignore(x => x.DomainEvents);
            b.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Events).HasField("_events").UsePropertyAccessMode(PropertyAccessMode.Field);
            b.HasIndex(x => x.OrderId).IsUnique().HasDatabaseName("ux_shipping_shipment_order");
            b.HasIndex(x => new { x.SellerId, x.Status }).HasDatabaseName("ix_shipping_shipment_seller_status");
            b.HasIndex(x => new { x.BuyerId, x.Status }).HasDatabaseName("ix_shipping_shipment_buyer_status");
            b.HasIndex(x => x.TrackingCode).HasFilter("tracking_code IS NOT NULL").HasDatabaseName("ix_shipping_shipment_tracking");
        });

        modelBuilder.Entity<ShipmentEvent>(b =>
        {
            b.ToTable("shipment_events");
            b.HasKey(x => new { x.ShipmentId, x.OccurredAt });
            b.Property(x => x.Status).HasMaxLength(30).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.TrackingCode).HasMaxLength(100);
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
        var shipments = ChangeTracker.Entries<Shipment>()
            .Select(x => x.Entity).Where(x => x.DomainEvents.Count > 0).ToArray();
        var events = shipments.SelectMany(x => x.DomainEvents).ToArray();

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
            foreach (var aggregate in shipments) aggregate.ClearDomainEvents();
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Shipment record changed. Reload and retry.");
        }
    }

    private static string EventType(IDomainEvent domainEvent) => domainEvent switch
    {
        ShipmentCreatedDomainEvent => "shipping.shipment-created.v1",
        ShipmentTrackingUpdatedDomainEvent => "shipping.tracking-updated.v1",
        ShipmentDeliveredDomainEvent => "shipping.shipment-delivered.v1",
        _ => throw new InvalidOperationException("Unmapped shipping domain event.")
    };
}