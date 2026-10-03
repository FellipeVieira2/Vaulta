namespace Vaulta.Vision.Domain;
public sealed class ScanAttempt
{
 public Guid Id {get;set;} public Guid OwnerId {get;set;} public Guid? SessionCorrelationId {get;set;}
 public DateTimeOffset CreatedAt {get;set;} public DateTimeOffset RetentionUntil {get;set;} public DateTimeOffset? DeletionRequestedAt {get;set;}
 public string OperationalPolicyVersion {get;set;}=null!; public string? ImprovementPolicyVersion {get;set;} public string Status {get;set;}="active";
}
public sealed class ScanCapture
{
 public Guid Id {get;set;} public Guid AttemptId {get;set;} public Guid AssetId {get;set;} public int Sequence {get;set;} public string Role {get;set;}=null!;
 public string Sha256 {get;set;}=null!; public string Status {get;set;}="pending"; public DateTimeOffset CreatedAt {get;set;} public DateTimeOffset? ConfirmedAt {get;set;}
}
public sealed class ScanRun
{
 public Guid Id {get;set;} public Guid AttemptId {get;set;} public string ExecutionKey {get;set;}=null!; public string InputSha256 {get;set;}=null!;
 public string Status {get;set;}="running"; public DateTimeOffset StartedAt {get;set;} public DateTimeOffset? CompletedAt {get;set;}
 public string? PredictionJson {get;set;} public string? ManifestJson {get;set;} public float[]? Embedding {get;set;}
}
public sealed class ScanRunCandidate
{
 public Guid RunId {get;set;} public int Rank {get;set;} public Guid PrintingId {get;set;} public double? RetrievalScore {get;set;}
}
public sealed class VisionFeedback
{
 public Guid Id {get;set;} public Guid AttemptId {get;set;} public Guid RunId {get;set;} public string IdempotencyKey {get;set;}=null!;
 public string Source {get;set;}=null!; public Guid? PrintingId {get;set;} public Guid? VariantId {get;set;}
 public string? Orientation {get;set;} public string? Presence {get;set;} public string? Notes {get;set;} public DateTimeOffset CreatedAt {get;set;}
}
public sealed class VisionReviewedSample
{
 public Guid Id {get;set;} public Guid AttemptId {get;set;} public Guid FeedbackId {get;set;} public Guid CaptureId {get;set;}
 public DateTimeOffset ReviewedAt {get;set;} public string ReviewSource {get;set;}="operator-cli";
}
public sealed class VisionFrozenBenchmark
{
 public string Version {get;set;}=null!; public string ManifestJson {get;set;}=null!; public DateTimeOffset CreatedAt {get;set;}
}
