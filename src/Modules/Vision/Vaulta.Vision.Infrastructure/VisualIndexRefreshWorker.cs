using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vaulta.Vision.Application;
namespace Vaulta.Vision.Infrastructure;
internal sealed class VisualIndexRefreshWorker(IServiceScopeFactory scopes,VisionOptions options,VisionImprovementOptions improvement,VisualIndexRefreshSignal signal,ILogger<VisualIndexRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(string.IsNullOrWhiteSpace(options.ModelManifestPath)) return;
        while(!stoppingToken.IsCancellationRequested)
        {
            var revision=signal.RequestedRevision;
            try { await using var scope=scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<IVisualReferenceBuilder>().LoadAsync(stoppingToken); signal.Completed(revision,DateTimeOffset.UtcNow); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
            catch(Exception error) { logger.LogWarning("Visual index refresh unavailable: {ErrorType}",error.GetType().Name); }
            try { await signal.WaitAsync(TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(improvement.RefreshDebounceSeconds),stoppingToken); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
