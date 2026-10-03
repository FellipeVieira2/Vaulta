using Microsoft.EntityFrameworkCore;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Infrastructure;
public sealed class VisionDbContext(DbContextOptions<VisionDbContext> options) : DbContext(options)
{
    public DbSet<VisualReference> VisualReferences=>Set<VisualReference>();
    public DbSet<ScanAttempt> ScanAttempts=>Set<ScanAttempt>();
    public DbSet<ScanCapture> ScanCaptures=>Set<ScanCapture>();
    public DbSet<ScanRun> ScanRuns=>Set<ScanRun>();
    public DbSet<ScanRunCandidate> ScanRunCandidates=>Set<ScanRunCandidate>();
    public DbSet<VisionFeedback> Feedback=>Set<VisionFeedback>();
    public DbSet<VisionReviewedSample> ReviewedSamples=>Set<VisionReviewedSample>();
    public DbSet<VisionFrozenBenchmark> FrozenBenchmarks=>Set<VisionFrozenBenchmark>();
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
        model.Entity<ScanAttempt>(b=> { b.ToTable("scan_attempts"); b.HasKey(x=>x.Id); b.HasIndex(x=>new{x.OwnerId,x.CreatedAt}); b.HasIndex(x=>x.RetentionUntil); b.Property(x=>x.OperationalPolicyVersion).HasMaxLength(100); b.Property(x=>x.ImprovementPolicyVersion).HasMaxLength(100); b.Property(x=>x.Status).HasMaxLength(32); });
        model.Entity<ScanCapture>(b=> { b.ToTable("scan_captures"); b.HasKey(x=>x.Id); b.HasOne<ScanAttempt>().WithMany().HasForeignKey(x=>x.AttemptId); b.HasIndex(x=>new{x.AttemptId,x.Sequence}).IsUnique(); b.HasIndex(x=>x.AssetId).IsUnique(); b.Property(x=>x.Sha256).HasMaxLength(64); b.Property(x=>x.Role).HasMaxLength(32); b.Property(x=>x.Status).HasMaxLength(32); });
        model.Entity<ScanRun>(b=> { b.ToTable("scan_runs"); b.HasKey(x=>x.Id); b.HasOne<ScanAttempt>().WithMany().HasForeignKey(x=>x.AttemptId); b.HasIndex(x=>new{x.AttemptId,x.ExecutionKey}).IsUnique(); b.Property(x=>x.ExecutionKey).HasMaxLength(128); b.Property(x=>x.InputSha256).HasMaxLength(64); b.Property(x=>x.Status).HasMaxLength(32); b.Property(x=>x.PredictionJson).HasColumnType("jsonb"); b.Property(x=>x.ManifestJson).HasColumnType("jsonb"); b.Property(x=>x.Embedding).HasColumnType("real[]"); });
        model.Entity<ScanRunCandidate>(b=> { b.ToTable("scan_run_candidates"); b.HasKey(x=>new{x.RunId,x.Rank}); b.HasOne<ScanRun>().WithMany().HasForeignKey(x=>x.RunId); });
        model.Entity<VisionFeedback>(b=> { b.ToTable("feedback"); b.HasKey(x=>x.Id); b.HasOne<ScanAttempt>().WithMany().HasForeignKey(x=>x.AttemptId); b.HasOne<ScanRun>().WithMany().HasForeignKey(x=>x.RunId); b.HasIndex(x=>new{x.AttemptId,x.IdempotencyKey}).IsUnique(); b.Property(x=>x.IdempotencyKey).HasMaxLength(128); b.Property(x=>x.Source).HasMaxLength(32); b.Property(x=>x.Orientation).HasMaxLength(32); b.Property(x=>x.Presence).HasMaxLength(32); b.Property(x=>x.Notes).HasMaxLength(1000); });
        model.Entity<VisionReviewedSample>(b=> { b.ToTable("reviewed_samples"); b.HasKey(x=>x.Id); b.HasOne<ScanAttempt>().WithMany().HasForeignKey(x=>x.AttemptId); b.HasOne<VisionFeedback>().WithMany().HasForeignKey(x=>x.FeedbackId); b.HasOne<ScanCapture>().WithMany().HasForeignKey(x=>x.CaptureId); b.HasIndex(x=>new{x.FeedbackId,x.CaptureId}).IsUnique(); b.Property(x=>x.ReviewSource).HasMaxLength(32); });
        model.Entity<VisionFrozenBenchmark>(b=> { b.ToTable("frozen_benchmarks"); b.HasKey(x=>x.Version); b.Property(x=>x.Version).HasMaxLength(128); b.Property(x=>x.ManifestJson).HasColumnType("jsonb"); });
        foreach(var entity in model.Model.GetEntityTypes()) foreach(var property in entity.GetProperties()) property.SetColumnName(string.Concat(property.Name.Select((c,i)=>char.IsUpper(c)&&i>0 ? "_"+char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}
