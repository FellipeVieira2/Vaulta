using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vaulta.Assets.Application;
using Vaulta.Assets.Contracts;
using Vaulta.SharedKernel;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Infrastructure;
public sealed class VisionHistoryService(VisionDbContext db,IPrivateAssetService assets,IVisionCatalog catalog,IClock clock,VisionHistoryOptions options)
{
 private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 public VisionPoliciesDto Policies=>new(options.Enabled,options.OperationalPolicyVersion,options.ImprovementPolicyVersion,options.RetentionDays);
 public async Task<CreateScanAttemptResponse> CreateAsync(Guid owner,CreateScanAttemptRequest request,CancellationToken ct)
 {
  if(!options.Enabled || owner==Guid.Empty || request.OperationalPolicyVersion!=options.OperationalPolicyVersion || request.ImprovementPolicyVersion is not null && request.ImprovementPolicyVersion!=options.ImprovementPolicyVersion) throw new DomainException("Accept the current storage policy before creating an attempt.");
  var row=new ScanAttempt {Id=Guid.NewGuid(),OwnerId=owner,SessionCorrelationId=request.SessionCorrelationId,CreatedAt=clock.UtcNow,RetentionUntil=clock.UtcNow.AddDays(options.RetentionDays),OperationalPolicyVersion=request.OperationalPolicyVersion,ImprovementPolicyVersion=request.ImprovementPolicyVersion};
  db.ScanAttempts.Add(row);await db.SaveChangesAsync(ct);return new(row.Id,row.RetentionUntil);
 }
 private async Task<ScanAttempt> Owned(Guid owner,Guid id,CancellationToken ct,bool allowExpired=false)
 {
  var row=await db.ScanAttempts.SingleOrDefaultAsync(x=>x.Id==id && x.OwnerId==owner,ct)??throw new NotFoundException("Attempt not found.");
  if(!allowExpired && (!options.Enabled || row.OperationalPolicyVersion!=options.OperationalPolicyVersion || row.RetentionUntil<=clock.UtcNow || row.DeletionRequestedAt is not null)) throw new ConflictException("Attempt expired or withdrawn.");return row;
 }
 private async Task Lock(Guid attempt,CancellationToken ct)=>await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({attempt.ToString()}))",ct);
 public async Task<ScanRun> ReserveAsync(Guid owner,Guid attempt,string key,string sha,CancellationToken ct)
 {
  await Owned(owner,attempt,ct);VisionDatasetRules.ValidateKey(key);sha=VisionDatasetRules.Sha(sha);
  await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(attempt,ct);
  var prior=await db.ScanRuns.SingleOrDefaultAsync(x=>x.AttemptId==attempt && x.ExecutionKey==key,ct);
  if(prior is not null)
  { if(prior.InputSha256!=sha || prior.Status!="completed") throw new ConflictException("Execution is in progress, failed, or belongs to another image.");await tx.CommitAsync(ct);return prior; }
  if(await db.ScanRuns.CountAsync(x=>x.AttemptId==attempt,ct)>=20) throw new DomainException("Attempt execution limit reached.");
  var run=new ScanRun {Id=Guid.NewGuid(),AttemptId=attempt,ExecutionKey=key,InputSha256=sha,StartedAt=clock.UtcNow};db.ScanRuns.Add(run);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return run;
 }
 public async Task CompleteAsync(Guid runId,VisionScanResultDto result,ImageEmbedding embedding,CancellationToken ct)
 {
  var run=await db.ScanRuns.SingleAsync(x=>x.Id==runId,ct);if(run.Status!="running") throw new ConflictException("Run already completed.");
  VisionDatasetRules.ValidateConfidence(result.PrintingConfidence);VisionDatasetRules.ValidateConfidence(result.VariantConfidence);
  if(embedding.Identity!=result.Trace.Encoder || embedding.Vector.Length!=embedding.Identity.Dimension) throw new DomainException("Run embedding does not match its executed manifest.");
  run.PredictionJson=JsonSerializer.Serialize(result with {Price=null,PriceStatus=result.PriceStatus=="available"?"unavailable":result.PriceStatus,History=null},Json);
  run.ManifestJson=JsonSerializer.Serialize(result.Trace,Json);run.Embedding=EmbeddingMath.Normalize(embedding.Vector,embedding.Identity.Dimension);run.Status="completed";run.CompletedAt=clock.UtcNow;
  db.ScanRunCandidates.AddRange(result.Candidates.Take(10).Select((c,i)=>new ScanRunCandidate {RunId=runId,Rank=i+1,PrintingId=c.Printing.PrintingId,RetrievalScore=c.RetrievalScore}));await db.SaveChangesAsync(ct);
 }
 public async Task<VisionAttemptDetailsDto> GetAsync(Guid owner,Guid attempt,CancellationToken ct)
 {
  var a=await Owned(owner,attempt,ct,true);var captures=await db.ScanCaptures.AsNoTracking().Where(x=>x.AttemptId==attempt).OrderBy(x=>x.Sequence).Take(3).ToArrayAsync(ct);
  var runs=await db.ScanRuns.AsNoTracking().Where(x=>x.AttemptId==attempt).OrderBy(x=>x.StartedAt).Take(20).ToArrayAsync(ct);
  var feedback=await db.Feedback.AsNoTracking().Where(x=>x.AttemptId==attempt).OrderBy(x=>x.CreatedAt).Take(100).ToArrayAsync(ct);
  return new(a.Id,a.RetentionUntil,a.Status,captures.Select(x=>new VisionCaptureDto(x.Id,x.AssetId,x.Sequence,x.Role,x.Sha256,x.Status)).ToArray(),runs.Select(x=>new VisionRunDto(x.Id,x.Status,x.InputSha256,x.PredictionJson is null?null:JsonSerializer.Deserialize<VisionScanResultDto>(x.PredictionJson,Json),x.Embedding is not null)).ToArray(),feedback.Select(x=>new VisionFeedbackDto(x.Id,new(x.RunId,x.Source,x.PrintingId,x.VariantId,x.Orientation,x.Presence,x.Notes),x.CreatedAt)).ToArray());
 }
 public async Task<VisionCaptureUploadResponse> UploadAsync(Guid owner,Guid attempt,CreateVisionCaptureRequest request,IAssetService uploads,CancellationToken ct)
 {
  await Owned(owner,attempt,ct);var sha=VisionDatasetRules.Sha(request.Sha256);
  if(request.Sequence is <0 or >2 || request.Role is not ("full-frame" or "card-crop" or "finish")) throw new DomainException("Invalid capture sequence/role.");
  await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(attempt,ct);
  if(await db.ScanCaptures.AnyAsync(x=>x.AttemptId==attempt && x.Sequence==request.Sequence,ct)) throw new ConflictException("Capture sequence already reserved.");
  var upload=await uploads.CreateUpload(owner,new("vision-scan",request.ContentType,request.ContentLength,sha),ct);
  var row=new ScanCapture {Id=Guid.NewGuid(),AttemptId=attempt,AssetId=upload.AssetId,Sequence=request.Sequence,Role=request.Role,Sha256=sha,CreatedAt=clock.UtcNow};db.ScanCaptures.Add(row);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(row.Id,row.AssetId,upload.UploadUrl,upload.ExpiresAt);
 }
 public async Task<VisionCaptureDto> ConfirmAsync(Guid owner,Guid attempt,Guid capture,IAssetService uploads,CancellationToken ct)
 {
  await Owned(owner,attempt,ct);var row=await db.ScanCaptures.SingleOrDefaultAsync(x=>x.Id==capture && x.AttemptId==attempt,ct)??throw new NotFoundException("Capture not found.");
  var details=await assets.GetOwnedAsync(owner,row.AssetId,ct);if(details is null || details.Purpose!="vision-scan" || details.Sha256!=row.Sha256) throw new DomainException("Capture asset mismatch.");
  await uploads.ConfirmUpload(owner,row.AssetId,ct);details=await assets.GetOwnedAsync(owner,row.AssetId,ct);
  if(details?.Status!="ready" || details.Sha256!=row.Sha256) throw new DomainException("Capture is not verified in storage.");
  row.Status="ready";row.ConfirmedAt??=clock.UtcNow;await db.SaveChangesAsync(ct);return new(row.Id,row.AssetId,row.Sequence,row.Role,row.Sha256,row.Status);
 }
 public async Task<VisionFeedbackDto> FeedbackAsync(Guid owner,Guid attempt,string key,VisionFeedbackRequest request,CancellationToken ct)
 {
  await Owned(owner,attempt,ct);VisionDatasetRules.ValidateKey(key);VisionDatasetRules.ValidateFeedback(request.Source,request.ConfirmedPrintingId,request.ConfirmedVariantId,request.Orientation,request.Presence,request.Notes);
  if(!await db.ScanRuns.AnyAsync(x=>x.Id==request.RunId && x.AttemptId==attempt && x.Status=="completed",ct)) throw new DomainException("Feedback must match a completed run.");
  if(request.ConfirmedPrintingId is { } p)
  { var canonical=(await catalog.GetPrintingsAsync([p],ct)).SingleOrDefault();if(canonical is null || request.ConfirmedVariantId is { } v && !canonical.Printing.Variants.Any(x=>x.Id==v)) throw new DomainException("Printing/variant does not exist or does not match."); }
  await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(attempt,ct);
  var prior=await db.Feedback.SingleOrDefaultAsync(x=>x.AttemptId==attempt && x.IdempotencyKey==key,ct);
  if(prior is not null)
  { var previous=new VisionFeedbackRequest(prior.RunId,prior.Source,prior.PrintingId,prior.VariantId,prior.Orientation,prior.Presence,prior.Notes);if(previous!=request) throw new ConflictException("Feedback key already has different content.");await tx.CommitAsync(ct);return new(prior.Id,previous,prior.CreatedAt); }
  if(await db.Feedback.CountAsync(x=>x.AttemptId==attempt,ct)>=100) throw new DomainException("Feedback limit reached.");
  var row=new VisionFeedback {Id=Guid.NewGuid(),AttemptId=attempt,RunId=request.RunId,IdempotencyKey=key,Source=request.Source,PrintingId=request.ConfirmedPrintingId,VariantId=request.ConfirmedVariantId,Orientation=request.Orientation,Presence=request.Presence,Notes=request.Notes,CreatedAt=clock.UtcNow};db.Feedback.Add(row);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(row.Id,request,row.CreatedAt);
 }
 public async Task DeleteAsync(Guid owner,Guid attempt,CancellationToken ct)
 {
  var a=await Owned(owner,attempt,ct,true);a.DeletionRequestedAt??=clock.UtcNow;a.Status="deletion_pending";await db.SaveChangesAsync(ct);
  var captures=await db.ScanCaptures.Where(x=>x.AttemptId==attempt).ToArrayAsync(ct);var ids=captures.Select(x=>x.AssetId).ToArray();
  await db.VisualReferences.Where(x=>x.Origin=="verified_capture" && x.SourceAssetId!=null && ids.Contains(x.SourceAssetId.Value)).ExecuteDeleteAsync(ct);
  foreach(var capture in captures) { await assets.DeleteOwnedAsync(owner,capture.AssetId,ct);capture.Status="deleted"; }
  foreach(var run in await db.ScanRuns.Where(x=>x.AttemptId==attempt).ToArrayAsync(ct)) { run.Embedding=null;run.PredictionJson=null;run.ManifestJson=null; }
  await db.ReviewedSamples.Where(x=>x.AttemptId==attempt).ExecuteDeleteAsync(ct);await db.Feedback.Where(x=>x.AttemptId==attempt).ExecuteDeleteAsync(ct);
  // A signed PUT can outlive logical withdrawal. Repeat physical deletion until every issued URL has expired.
  a.Status=captures.Any(x=>x.CreatedAt.AddMinutes(11)>clock.UtcNow)?"deletion_pending":"deleted";await db.SaveChangesAsync(ct);
 }
 public async Task<int> PurgeExpiredAsync(CancellationToken ct)
 { var expired=await db.ScanAttempts.AsNoTracking().Where(x=>x.RetentionUntil<=clock.UtcNow && x.Status!="deleted" || x.Status=="deletion_pending").OrderBy(x=>x.RetentionUntil).Take(100).Select(x=>new{x.Id,x.OwnerId}).ToArrayAsync(ct);foreach(var row in expired) await DeleteAsync(row.OwnerId,row.Id,ct);return expired.Length; }
 public async Task<Guid> PromoteAsync(Guid attempt,Guid feedback,CancellationToken ct)
 {
  var a=await db.ScanAttempts.SingleOrDefaultAsync(x=>x.Id==attempt,ct)??throw new NotFoundException("Attempt not found.");await Owned(a.OwnerId,attempt,ct);
  var f=await db.Feedback.SingleOrDefaultAsync(x=>x.Id==feedback && x.AttemptId==attempt,ct)??throw new NotFoundException("Feedback not found.");
  var run=await db.ScanRuns.SingleAsync(x=>x.Id==f.RunId,ct);var capture=await db.ScanCaptures.Where(x=>x.AttemptId==attempt && x.Status=="ready" && x.Sha256==run.InputSha256).OrderBy(x=>x.Sequence).FirstOrDefaultAsync(ct);
  VisionDatasetRules.RequirePromotion(a.ImprovementPolicyVersion==options.ImprovementPolicyVersion && a.ImprovementPolicyVersion is not null,f.Source is "UserConfirmation" or "UserCorrection",capture is not null);
  var asset=await assets.GetOwnedAsync(a.OwnerId,capture!.AssetId,ct);if(asset?.Status!="ready" || asset.Purpose!="vision-scan" || asset.Sha256!=run.InputSha256) throw new DomainException("Verified capture is unavailable.");
  await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(attempt,ct);
  var prior=await db.ReviewedSamples.SingleOrDefaultAsync(x=>x.FeedbackId==feedback && x.CaptureId==capture.Id,ct);if(prior is not null) {await tx.CommitAsync(ct);return prior.Id;}
  var sample=new VisionReviewedSample {Id=Guid.NewGuid(),AttemptId=attempt,FeedbackId=feedback,CaptureId=capture.Id,ReviewedAt=clock.UtcNow};db.ReviewedSamples.Add(sample);
  await db.VisualReferences.Where(x=>x.Origin=="verified_capture" && x.SourceAssetId==capture.AssetId).ExecuteDeleteAsync(ct);
  if(f.PrintingId is { } printing && f.Orientation=="front" && f.Presence=="card-present" && run.Embedding is not null)
  {
   var canonical=(await catalog.GetPrintingsAsync([printing],ct)).SingleOrDefault();if(canonical is null || f.VariantId is { } v && !canonical.Printing.Variants.Any(x=>x.Id==v)) throw new DomainException("Reviewed identity is no longer active.");
   var trace=JsonSerializer.Deserialize<VisionScanTraceDto>(run.ManifestJson!,Json)!;var vector=EmbeddingMath.Normalize(run.Embedding,trace.Encoder.Dimension);
   var version=VisualReferenceBuilder.ModelVersion(trace.Encoder);
   // Each asset has at most one live identity per model. A later correction replaces its reference, never the run/feedback.
   await db.VisualReferences.Where(x=>x.Origin=="verified_capture" && x.SourceAssetId==capture.AssetId && x.ModelVersion==version).ExecuteDeleteAsync(ct);
   db.VisualReferences.Add(new VisualReference {Id=Guid.NewGuid(),PrintingId=printing,SourceAssetId=capture.AssetId,Origin="verified_capture",Status="ready",ModelVersion=version,ModelManifestJson=JsonSerializer.Serialize(trace.Encoder),Dimension=trace.Encoder.Dimension,Vector=vector,ImageSha256=run.InputSha256,VectorSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(vector.AsSpan()))).ToLowerInvariant(),CreatedAt=clock.UtcNow,UpdatedAt=clock.UtcNow});
  }
  await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return sample.Id;
 }
 public async Task<VisionBenchmarkManifestDto> ExportAsync(string version,CancellationToken ct)
 {
  VisionDatasetRules.ValidateKey(version);var frozen=await db.FrozenBenchmarks.SingleOrDefaultAsync(x=>x.Version==version,ct);
  if(frozen is not null)
  {
   var stored=JsonSerializer.Deserialize<VisionBenchmarkManifestDto>(frozen.ManifestJson,Json)!;var ids=stored.Samples.Select(x=>x.SampleId).ToArray();
   var active=await (from sample in db.ReviewedSamples join a in db.ScanAttempts on sample.AttemptId equals a.Id where ids.Contains(sample.Id) && a.DeletionRequestedAt==null && a.RetentionUntil>clock.UtcNow select sample.Id).CountAsync(ct);
   if(active!=ids.Length)throw new ConflictException("Frozen benchmark contains withdrawn/expired samples; create a new version.");return stored;
  }
  var rows=await (from sample in db.ReviewedSamples join a in db.ScanAttempts on sample.AttemptId equals a.Id join c in db.ScanCaptures on sample.CaptureId equals c.Id join f in db.Feedback on sample.FeedbackId equals f.Id where a.DeletionRequestedAt==null && a.RetentionUntil>clock.UtcNow && c.Status=="ready" orderby sample.ReviewedAt descending,sample.Id select new {sample.AttemptId,sample.ReviewedAt,Sample=new VisionBenchmarkSampleDto(sample.Id,c.Id,c.Sha256,c.Sha256,f.PrintingId,f.VariantId,f.Orientation,f.Presence,f.Id)}).Take(10001).ToArrayAsync(ct);
  if(rows.Length>10000)throw new DomainException("Benchmark limit exceeded; select a smaller evaluation scope.");
  var latest=rows.GroupBy(x=>x.Sample.CaptureId).Select(x=>x.First()).ToArray();
  var groups=VisionBenchmarkGrouping.Assign(latest.Select(x=>(x.AttemptId,x.Sample.Sha256)).ToArray());
  var samples=latest.Select(x=>x.Sample with {SplitGroup=groups[(x.AttemptId,x.Sample.Sha256)]}).OrderBy(x=>x.SampleId).ToArray();
  var manifest=new VisionBenchmarkManifestDto(version,clock.UtcNow,samples);db.FrozenBenchmarks.Add(new() {Version=version,CreatedAt=clock.UtcNow,ManifestJson=JsonSerializer.Serialize(manifest,Json)});await db.SaveChangesAsync(ct);return manifest;
 }
 public static VisionScanResultDto Replay(ScanRun run)=>JsonSerializer.Deserialize<VisionScanResultDto>(run.PredictionJson!,Json)!;
}
