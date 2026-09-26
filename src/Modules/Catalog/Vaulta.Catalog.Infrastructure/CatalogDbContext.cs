using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Catalog.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Set> Sets => Set<Set>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Printing> Printings => Set<Printing>();
    public DbSet<Variant> Variants => Set<Variant>();
    public DbSet<CatalogExternalId> ExternalIds => Set<CatalogExternalId>();
    public DbSet<CatalogSyncRun> SyncRuns => Set<CatalogSyncRun>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");
        modelBuilder.Entity<Game>().HasData(new Game
        {
            Id = Guid.Parse("f18fd4d1-2514-4b19-9eaa-f33c04564c7b"),
            Code = "pokemon",
            Name = "Pokémon"
        });
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages", "identity", table => table.ExcludeFromMigrations());
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(150).IsRequired();
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Error).HasColumnType("text");
            b.HasIndex(x => x.OccurredAt).HasFilter("processed_at IS NULL");
        });
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties()) property.SetColumnName(Snake(property.Name));
    }

    private static string Snake(string value) => string.Concat(value.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
