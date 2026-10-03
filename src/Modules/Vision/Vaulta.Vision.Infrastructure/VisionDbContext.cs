using Microsoft.EntityFrameworkCore;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Infrastructure;
public sealed class VisionDbContext(DbContextOptions<VisionDbContext> options) : DbContext(options)
{
    public DbSet<VisualReference> VisualReferences=>Set<VisualReference>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("vision");
        model.Entity<VisualReference>(b=>
        {
            b.ToTable("visual_references"); b.HasKey(x=>x.Id); b.Property(x=>x.Id).ValueGeneratedNever();
            b.Property(x=>x.ModelVersion).HasMaxLength(64); b.Property(x=>x.ModelManifestJson).HasColumnType("jsonb");
            b.Property(x=>x.Origin).HasMaxLength(32); b.Property(x=>x.Status).HasMaxLength(32); b.Property(x=>x.ImageSha256).HasMaxLength(64);
            b.Property(x=>x.OriginalSourceSha256).HasMaxLength(64); b.Property(x=>x.VectorSha256).HasMaxLength(64); b.Property(x=>x.Vector).HasColumnType("real[]"); b.Property(x=>x.ErrorCategory).HasMaxLength(80);
            b.HasIndex(x=>new { x.PrintingId,x.ModelVersion,x.SourceAssetId,x.Origin }).IsUnique(); b.HasIndex(x=>new { x.ModelVersion,x.Status });
        });
        foreach(var entity in model.Model.GetEntityTypes()) foreach(var property in entity.GetProperties()) property.SetColumnName(string.Concat(property.Name.Select((c,i)=>char.IsUpper(c)&&i>0 ? "_"+char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}
