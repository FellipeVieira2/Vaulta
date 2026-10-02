using Microsoft.EntityFrameworkCore;
using Vaulta.Marketplace.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Marketplace.Infrastructure;

public sealed class MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options) : DbContext(options)
{
    public DbSet<SellerProfile> SellerProfiles => Set<SellerProfile>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingPhoto> ListingPhotos => Set<ListingPhoto>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("marketplace");

        modelBuilder.Entity<SellerProfile>(b =>
        {
            b.ToTable("seller_profiles");
            b.HasKey(x => x.UserId);
            b.Property(x => x.UserId).ValueGeneratedNever();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.Bio).HasMaxLength(500);
            b.Property(x => x.Street).HasMaxLength(200);
            b.Property(x => x.City).HasMaxLength(200);
            b.Property(x => x.State).HasMaxLength(200);
            b.Property(x => x.ZipCode).HasMaxLength(10);
            b.Property(x => x.AverageRating).HasPrecision(5, 4);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.Ignore(x => x.DomainEvents);
            b.HasIndex(x => x.Status).HasDatabaseName("ix_marketplace_seller_status");
        });

        modelBuilder.Entity<Listing>(b =>
        {
            b.ToTable("listings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.SellerUserId).IsRequired();
            b.Property(x => x.CollectibleItemId).IsRequired();
            b.Property(x => x.PrintingId).IsRequired();
            b.Property(x => x.Condition).HasMaxLength(32).IsRequired();
            b.Property(x => x.PriceBrl).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.ClientDraftKey).HasMaxLength(128);
            b.Property(x => x.DraftCreationFingerprint).HasMaxLength(64);
            b.Property(x => x.PublicationKey).HasMaxLength(128);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.Ignore(x => x.DomainEvents);
            b.HasMany(x => x.Photos).WithOne().HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Photos).HasField("_photos").UsePropertyAccessMode(PropertyAccessMode.Field);
            b.HasIndex(x => new { x.SellerUserId, x.Status }).HasDatabaseName("ix_marketplace_listings_seller_status");
            b.HasIndex(x => new { x.PrintingId, x.Status }).HasDatabaseName("ix_marketplace_listings_printing_status");
            b.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("ix_marketplace_listings_status_created");
            b.HasIndex(x => x.CollectibleItemId).IsUnique().HasFilter("status IN ('active', 'publishing')").HasDatabaseName("ux_marketplace_listings_active_item");
            b.HasIndex(x => new { x.SellerUserId, x.ClientDraftKey }).IsUnique().HasFilter("client_draft_key IS NOT NULL").HasDatabaseName("ux_marketplace_listing_draft_key");
        });

        modelBuilder.Entity<ListingPhoto>(b =>
        {
            b.ToTable("listing_photos");
            b.HasKey(x => new { x.ListingId, x.AssetId });
            b.Property(x => x.Type).HasMaxLength(16).IsRequired();
            b.HasIndex(x => new { x.ListingId, x.SortOrder }).HasDatabaseName("ix_marketplace_listing_photos_order");
            b.HasIndex(x => new { x.ListingId, x.IsPrimary }).IsUnique().HasFilter("is_primary = true").HasDatabaseName("ux_marketplace_listing_photo_primary");
            b.HasIndex(x => x.AssetId).IsUnique().HasDatabaseName("ux_marketplace_listing_photo_asset");
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
            foreach (var property in entity.GetProperties()) property.SetColumnName(Snake(property.Name));
    }

    private static string Snake(string value) => string.Concat(value.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var profiles = ChangeTracker.Entries<SellerProfile>().Select(x => x.Entity).Where(x => x.DomainEvents.Count > 0).ToArray();
        var listings = ChangeTracker.Entries<Listing>().Select(x => x.Entity).Where(x => x.DomainEvents.Count > 0).ToArray();
        var events = profiles.SelectMany(x => x.DomainEvents).Concat(listings.SelectMany(x => x.DomainEvents)).ToArray();
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
            foreach (var aggregate in profiles) aggregate.ClearDomainEvents();
            foreach (var aggregate in listings) aggregate.ClearDomainEvents();
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Record changed. Reload and retry.");
        }
    }

    private static string EventType(IDomainEvent domainEvent) => domainEvent switch
    {
        SellerProfileEnabledDomainEvent => "marketplace.seller-enabled.v1",
        SellerProfileSuspendedDomainEvent => "marketplace.seller-suspended.v1",
        ListingCreatedDomainEvent => "marketplace.listing-created.v1",
        ListingUpdatedDomainEvent => "marketplace.listing-updated.v1",
        ListingCancelledDomainEvent => "marketplace.listing-cancelled.v1",
        ListingSoldDomainEvent => "marketplace.listing-sold.v1",
        _ => throw new InvalidOperationException("Unmapped marketplace domain event.")
    };
}
