using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Application;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Infrastructure;
namespace Vaulta.Web.Api;
public sealed class ScannerVisionCoordinator(VisionScannerService scanner,VisionHistoryService history,VisionDbContext db,IScannerCardDetailsReader prices,ILogger<ScannerVisionCoordinator> logger)
{
 public async Task<VisionScanResultDto> IdentifyAsync(Guid owner,byte[] image,string? game,Guid? attempt,string? executionKey,CancellationToken ct)
 {
  if((attempt is null)!=(executionKey is null)) throw new Vaulta.SharedKernel.DomainException("Attempt and execution key must be supplied together.");
  if(attempt is null) return await scanner.IdentifyAsync(new([new(image)],game),ct);
  var run=await history.ReserveAsync(owner,attempt.Value,executionKey!,Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant(),ct);
  if(run.Status=="completed")
  {
   var replay=VisionHistoryService.Replay(run);
   if(replay.PrintingId is { } p && replay.VariantId is { } v && replay.PriceStatus!="graded_unavailable")
   {var quote=(await prices.GetAsync(p,ct))?.MarketQuotes.SingleOrDefault(x=>x.VariantId==v);replay=replay with {Price=quote,PriceStatus=quote is null?"unavailable":"available"};}
   return replay with {History=new(attempt.Value,run.Id,"replayed")};
  }
  VisionScanExecution execution;
  try {execution=await scanner.ExecuteAsync(new([new(image)],game),ct);}
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
