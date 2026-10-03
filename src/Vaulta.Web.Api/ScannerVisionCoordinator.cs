using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Application;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Infrastructure;
namespace Vaulta.Web.Api;
public sealed class ScannerVisionCoordinator(VisionScannerService scanner,VisionHistoryService history,VisionDbContext db,IScannerCardDetailsReader prices,ILogger<ScannerVisionCoordinator> logger)
{
 public async Task<VisionScanResultDto> IdentifyAsync(Guid owner,byte[] image,string? game,Guid? attempt,string? executionKey,CancellationToken ct,byte[]? retrievalCrop=null)
 {
  if((attempt is null)!=(executionKey is null)) throw new Vaulta.SharedKernel.DomainException("Attempt and execution key must be supplied together.");
  var input=new VisionScanInput(retrievalCrop is null?[new(image,"full-frame")]:[new(retrievalCrop,"card-crop"),new(image,"full-frame")],game);
  if(attempt is null) return await scanner.IdentifyAsync(input,ct);
  var fingerprint=Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
  if(retrievalCrop is not null)
  {
   using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
   foreach(var capture in input.Captures) { hash.AppendData(System.Text.Encoding.UTF8.GetBytes(capture.Role!));hash.AppendData(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(capture.Image.Length)));hash.AppendData(capture.Image); }
   fingerprint=Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
  }
  Vaulta.Vision.Domain.ScanRun run;
  using(var reserveTimeout=CancellationTokenSource.CreateLinkedTokenSource(ct))
  {
   reserveTimeout.CancelAfter(TimeSpan.FromSeconds(2));
   try {run=await history.ReserveAsync(owner,attempt.Value,executionKey!,fingerprint,reserveTimeout.Token);}
   catch(Exception error) when(error is DbUpdateException or Npgsql.NpgsqlException or TimeoutException || error is OperationCanceledException && !ct.IsCancellationRequested)
   {
    db.ChangeTracker.Clear();logger.LogWarning("Vision reservation metadata unavailable: {ErrorType}",error.GetType().Name);
    var unsaved=await scanner.IdentifyAsync(input,ct);return unsaved with {History=new(attempt.Value,null,"failed")};
   }
  }
  if(run.Status=="completed")
  {
   var replay=VisionHistoryService.Replay(run);
   if(replay.PrintingId is { } p && replay.VariantId is { } v && replay.PriceStatus!="graded_unavailable")
   {var quote=(await prices.GetAsync(p,ct))?.MarketQuotes.SingleOrDefault(x=>x.VariantId==v);replay=replay with {Price=quote,PriceStatus=quote is null?"unavailable":"available"};}
   return replay with {History=new(attempt.Value,run.Id,"replayed")};
  }
  VisionScanExecution execution;
  try {execution=await scanner.ExecuteAsync(input,ct);}
  catch
  {
   run.Status="failed";using var failTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
   try {await db.SaveChangesAsync(failTimeout.Token);}catch(Exception error){logger.LogWarning("Vision run failure metadata unavailable: {ErrorType}",error.GetType().Name);}throw;
  }
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(2));
  try {await history.CompleteAsync(run.Id,execution.Result,execution.Embedding,timeout.Token);return execution.Result with {History=new(attempt.Value,run.Id,"saved")};}
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch(Exception error){db.ChangeTracker.Clear();logger.LogWarning("Vision result metadata unavailable: {ErrorType}",error.GetType().Name);return execution.Result with {History=new(attempt.Value,run.Id,"failed")};}
 }
}
