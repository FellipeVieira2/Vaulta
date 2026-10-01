using Microsoft.EntityFrameworkCore;
using Vaulta.Collection.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Collection.Infrastructure;

public sealed class CollectionDbContext(DbContextOptions<CollectionDbContext> options) : DbContext(options)
{
    public DbSet<CollectionEntry> Entries => Set<CollectionEntry>();
    public DbSet<CollectibleItem> Items => Set<CollectibleItem>();
    public DbSet<CollectibleItemAsset> ItemAssets => Set<CollectibleItemAsset>();
    public DbSet<CollectionIdempotencyKey> IdempotencyKeys => Set<CollectionIdempotencyKey>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ItemOwnershipTransfer> OwnershipTransfers => Set<ItemOwnershipTransfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("collection");
        modelBuilder.Entity<CollectionEntry>(b =>
        {
            b.ToTable("collection_entries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.UserId).IsRequired();
            b.Property(x => x.PrintingId).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();
            b.Ignore(x => x.DomainEvents);
            b.HasAlternateKey(x => new { x.Id, x.UserId }).HasName("ak_collection_entries_id_user");
            b.HasIndex(x => new { x.UserId, x.PrintingId }).IsUnique().HasFilter("variant_id IS NULL").HasDatabaseName("ux_collection_entries_user_printing_no_variant");
            b.HasIndex(x => new { x.UserId, x.PrintingId, x.VariantId }).IsUnique().HasFilter("variant_id IS NOT NULL").HasDatabaseName("ux_collection_entries_user_printing_variant");
            b.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_collection_entries_user_created");
        });
        modelBuilder.Entity<CollectibleItem>(b =>
        {
            b.ToTable("collectible_items");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.CollectionEntryId).IsRequired();
            b.Property(x => x.UserId).IsRequired();
            b.Property(x => x.Condition).HasMaxLength(32).IsRequired();
            b.Property(x => x.AcquisitionAmount).HasPrecision(18, 4);
            b.Property(x => x.AcquisitionCurrency).HasMaxLength(3);
            b.Property(x => x.Notes).HasMaxLength(500);
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.Version).IsConcurrencyToken();
            b.Ignore(x => x.DomainEvents);
            b.HasOne<CollectionEntry>().WithMany().HasForeignKey(x => new { x.CollectionEntryId, x.UserId })
                .HasPrincipalKey(x => new { x.Id, x.UserId }).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Assets).WithOne().HasForeignKey(x => x.CollectibleItemId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Assets).HasField("_assets").UsePropertyAccessMode(PropertyAccessMode.Field);
            b.HasIndex(x => new { x.UserId, x.Status, x.CreatedAt }).HasDatabaseName("ix_collection_items_user_status_created");
            b.HasIndex(x => new { x.CollectionEntryId, x.Status }).HasDatabaseName("ix_collection_items_entry_status");
            b.HasIndex(x => new { x.UserId, x.Condition, x.Status }).HasDatabaseName("ix_collection_items_user_condition_status");
        });
        modelBuilder.Entity<CollectibleItemAsset>(b =>
        {
            b.ToTable("collectible_item_assets");
            b.HasKey(x => new { x.CollectibleItemId, x.AssetId });
            b.Property(x => x.Type).HasMaxLength(16).IsRequired();
            b.HasIndex(x => new { x.CollectibleItemId, x.SortOrder }).HasDatabaseName("ix_collection_item_assets_order");
            b.HasIndex(x => new { x.CollectibleItemId, x.IsPrimary }).IsUnique().HasFilter("is_primary = true").HasDatabaseName("ux_collection_item_asset_primary");
            b.HasIndex(x => x.AssetId).IsUnique().HasDatabaseName("ux_collection_item_asset_asset");
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
        modelBuilder.Entity<ItemOwnershipTransfer>(b =>
        {
            b.ToTable("item_ownership_transfers");
            b.HasKey(x => x.OrderId);
            b.Property(x => x.OrderId).ValueGeneratedNever();
            b.Property(x => x.PriceBrl).HasPrecision(18, 2);
            b.HasIndex(x => x.SellerItemId).IsUnique().HasDatabaseName("ux_collection_transfer_seller_item");
            b.HasIndex(x => x.BuyerItemId).IsUnique().HasDatabaseName("ux_collection_transfer_buyer_item");
            b.HasOne<CollectibleItem>().WithMany().HasForeignKey(x => x.SellerItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CollectibleItem>().WithMany().HasForeignKey(x => x.BuyerItemId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CollectionIdempotencyKey>(b =>
        {
            b.ToTable("idempotency_keys");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Operation).HasMaxLength(100).IsRequired();
            b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.ResponsePayload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
            b.HasIndex(x => new { x.UserId, x.Operation, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_collection_idempotency_keys_user_operation_key");
        });
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties()) property.SetColumnName(Snake(property.Name));
    }

    private static string Snake(string value) => string.Concat(value.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<CollectionEntry>().Select(x => x.Entity).Where(x => x.DomainEvents.Count > 0).ToArray();
        var items = ChangeTracker.Entries<CollectibleItem>().Select(x => x.Entity).Where(x => x.DomainEvents.Count > 0).ToArray();
        var events = entries.SelectMany(x => x.DomainEvents).Concat(items.SelectMany(x => x.DomainEvents)).ToArray();
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
            foreach (var aggregate in entries) aggregate.ClearDomainEvents();
            foreach (var aggregate in items) aggregate.ClearDomainEvents();
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Collectible item changed. Reload and retry.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } postgres &&
                                                    postgres.ConstraintName is "ux_collection_entries_user_printing_no_variant" or "ux_collection_entries_user_printing_variant")
        {
            throw new ConflictException("Collection entry was concurrently created. Retry the add operation.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } postgres &&
                                                    postgres.ConstraintName == "ux_collection_idempotency_keys_user_operation_key")
        {
            // Two concurrent requests raced past the advisory lock with the exact same Idempotency-Key
            // (e.g. two truly simultaneous retries). Neither response is authoritative yet; the caller
            // should retry so it observes the record persisted by whichever request committed first.
            throw new ConflictException("This Idempotency-Key is being processed concurrently. Retry the request.");
        }
    }

    private static string EventType(IDomainEvent domainEvent) => domainEvent switch
    {
        CollectibleItemsAddedDomainEvent => "collection.items-added.v1",
        CollectibleItemUpdatedDomainEvent => "collection.item-updated.v1",
        CollectibleItemRemovedDomainEvent => "collection.item-removed.v1",
        CollectibleItemAssetChangedDomainEvent => "collection.item-asset-changed.v1",
        CollectibleItemSoldDomainEvent => "collection.item-sold.v1",
        _ => throw new InvalidOperationException("Unmapped collection domain event.")
    };
}
