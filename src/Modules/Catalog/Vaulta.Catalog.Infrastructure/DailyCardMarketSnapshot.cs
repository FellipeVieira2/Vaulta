using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaulta.Catalog.Domain;

namespace Vaulta.Catalog.Infrastructure;

// Durable read model: keep successful daily quotes and their original FX provenance.
public sealed class DailyCardMarketSnapshot
{
    public Guid PrintingId { get; set; }
    public DateOnly MarketDay { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset RefreshAfter { get; set; }
    public string Payload { get; set; } = null!;
}

internal sealed class DailyCardMarketSnapshotConfiguration : IEntityTypeConfiguration<DailyCardMarketSnapshot>
{
    public void Configure(EntityTypeBuilder<DailyCardMarketSnapshot> b)
    {
        b.ToTable("daily_card_market_snapshots");
        b.HasKey(x => new { x.PrintingId, x.MarketDay });
        b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        b.HasOne<Printing>().WithMany().HasForeignKey(x => x.PrintingId).OnDelete(DeleteBehavior.Restrict);
    }
}
