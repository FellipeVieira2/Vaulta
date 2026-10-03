using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaulta.Catalog.Domain;

namespace Vaulta.Catalog.Infrastructure;

internal sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> b)
    {
        b.ToTable("games"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Code).HasMaxLength(32).IsRequired(); b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_catalog_games_code");
    }
}

internal sealed class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> b)
    {
        b.ToTable("series"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        b.HasOne(x => x.Game).WithMany(x => x.Series).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.GameId, x.NormalizedName }).IsUnique().HasDatabaseName("ux_catalog_series_game_name");
    }
}

internal sealed class SetConfiguration : IEntityTypeConfiguration<Set>
{
    public void Configure(EntityTypeBuilder<Set> b)
    {
        b.ToTable("sets"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Code).HasMaxLength(64); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ReleaseDate).HasMaxLength(10); b.Property(x => x.SymbolAssetKey).HasMaxLength(512); b.Property(x => x.LogoAssetKey).HasMaxLength(512);
        b.HasOne(x => x.Game).WithMany(x => x.Sets).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Series).WithMany(x => x.Sets).HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.GameId, x.Code }).IsUnique().HasFilter("code IS NOT NULL").HasDatabaseName("ux_catalog_sets_game_code");
        b.HasIndex(x => new { x.GameId, x.NormalizedName }).HasDatabaseName("ix_catalog_sets_game_name");
    }
}

internal sealed class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> b)
    {
        b.ToTable("cards"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Supertype).HasMaxLength(80); b.Property(x => x.Subtypes).HasMaxLength(500); b.Property(x => x.ImageAssetKey).HasMaxLength(512);
        b.HasOne(x => x.Game).WithMany(x => x.Cards).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.GameId, x.NormalizedName }).HasDatabaseName("ix_catalog_cards_game_name");
    }
}

internal sealed class PrintingConfiguration : IEntityTypeConfiguration<Printing>
{
    public void Configure(EntityTypeBuilder<Printing> b)
    {
        b.ToTable("printings"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.CollectorNumber).HasMaxLength(64).IsRequired(); b.Property(x => x.NormalizedCollectorNumber).HasMaxLength(64).IsRequired();
        b.Property(x => x.Language).HasMaxLength(35).IsRequired(); b.Property(x => x.Rarity).HasMaxLength(80); b.Property(x => x.RawRarity).HasMaxLength(120);
        b.Property(x => x.ExternalArtworkUrl).HasMaxLength(2048);
        b.Property(x => x.ArtworkProvider).HasMaxLength(40);
        b.Property(x => x.MetadataJson).HasColumnType("jsonb");
        b.Property(x => x.SourcePricingJson).HasColumnType("jsonb");
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasOne(x => x.Card).WithMany(x => x.Printings).HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Set).WithMany(x => x.Printings).HasForeignKey(x => x.SetId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SetId, x.NormalizedCollectorNumber, x.Language }).IsUnique().HasDatabaseName("ux_catalog_printings_set_number_language");
        b.HasIndex(x => x.CardId).HasDatabaseName("ix_catalog_printings_card");
        b.HasIndex(x => new { x.SetId, x.IsActive }).HasDatabaseName("ix_catalog_printings_set_active");
    }
}

internal sealed class VariantConfiguration : IEntityTypeConfiguration<Variant>
{
    public void Configure(EntityTypeBuilder<Variant> b)
    {
        b.ToTable("variants"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(120).IsRequired(); b.Property(x => x.RawValue).HasColumnType("text");
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasOne(x => x.Printing).WithMany(x => x.Variants).HasForeignKey(x => x.PrintingId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.PrintingId, x.Code }).IsUnique().HasDatabaseName("ux_catalog_variants_printing_code");
    }
}

internal sealed class CatalogExternalIdConfiguration : IEntityTypeConfiguration<CatalogExternalId>
{
    public void Configure(EntityTypeBuilder<CatalogExternalId> b)
    {
        b.ToTable("external_ids"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Provider).HasMaxLength(40).IsRequired(); b.Property(x => x.EntityType).HasMaxLength(40).IsRequired();
        b.Property(x => x.ExternalId).HasMaxLength(200).IsRequired(); b.Property(x => x.ContentHash).HasMaxLength(128);
        b.HasIndex(x => new { x.Provider, x.EntityType, x.ExternalId }).IsUnique().HasDatabaseName("ux_catalog_external_ids_source");
        b.HasIndex(x => new { x.EntityType, x.EntityId }).HasDatabaseName("ix_catalog_external_ids_entity");
    }
}

internal sealed class CatalogSyncRunConfiguration : IEntityTypeConfiguration<CatalogSyncRun>
{
    public void Configure(EntityTypeBuilder<CatalogSyncRun> b)
    {
        b.ToTable("sync_runs"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Provider).HasMaxLength(40).IsRequired(); b.Property(x => x.Scope).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasMaxLength(24).IsRequired(); b.Property(x => x.ErrorCategory).HasMaxLength(80);
        b.Property(x => x.ProgressJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.Provider, x.StartedAt }).HasDatabaseName("ix_catalog_sync_runs_provider_started");
    }
}
