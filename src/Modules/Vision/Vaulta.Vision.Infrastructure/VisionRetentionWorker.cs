using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace Vaulta.Vision.Infrastructure;
internal sealed class VisionRetentionWorker(IServiceScopeFactory scopes,ILogger<VisionRetentionWorker> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct)
 {
  while(!ct.IsCancellationRequested)
  {
   await Task.Delay(TimeSpan.FromMinutes(2),ct);
   try{await using var scope=scopes.CreateAsyncScope();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(45));await scope.ServiceProvider.GetRequiredService<VisionHistoryService>().PurgeExpiredAsync(timeout.Token);}
   catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
   catch(Exception error){logger.LogWarning("Vision retention retry needed: {ErrorType}",error.GetType().Name);}
  }
 }
}
