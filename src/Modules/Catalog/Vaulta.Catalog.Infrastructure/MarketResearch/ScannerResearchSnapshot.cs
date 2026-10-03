using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Vaulta.Catalog.Infrastructure.MarketResearch;

public sealed class ScannerResearchSnapshot
{
    public required string CacheKey { get; set; }
    public DateOnly MarketDay { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset RefreshAfter { get; set; }
    public required string Outcome { get; set; }
    public required string Payload { get; set; }
}

internal sealed class ScannerResearchSnapshotConfiguration : IEntityTypeConfiguration<ScannerResearchSnapshot>
{
    public void Configure(EntityTypeBuilder<ScannerResearchSnapshot> b)
    {
        b.ToTable("scanner_research_snapshots"); b.HasKey(x => x.CacheKey);
        b.Property(x => x.CacheKey).HasMaxLength(64).ValueGeneratedNever();
        b.Property(x => x.Outcome).HasMaxLength(24).IsRequired(); b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        b.HasIndex(x => x.RefreshAfter);
    }
}
