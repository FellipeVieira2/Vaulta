using Microsoft.EntityFrameworkCore;
using Vaulta.Assets.Domain;

namespace Vaulta.Assets.Infrastructure;

public sealed class AssetsDbContext(DbContextOptions<AssetsDbContext> options) : DbContext(options)
{
    public DbSet<Asset> Assets => Set<Asset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("assets");
        modelBuilder.Entity<Asset>(b =>
        {
            b.ToTable("assets");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ObjectKey).HasMaxLength(512).IsRequired();
            b.Property(x => x.Purpose).HasMaxLength(40).IsRequired();
            b.Property(x => x.Visibility).HasMaxLength(20).IsRequired();
            b.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            b.Property(x => x.SourceUrl).HasMaxLength(2048);
            b.Property(x => x.Sha256).HasMaxLength(64);
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.HasIndex(x => x.ObjectKey).IsUnique();
            b.HasIndex(x => new { x.OwnerId, x.CreatedAt });
            b.HasIndex(x => x.Status);
        });
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties()) property.SetColumnName(Snake(property.Name));
    }

    private static string Snake(string value) => string.Concat(value.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
