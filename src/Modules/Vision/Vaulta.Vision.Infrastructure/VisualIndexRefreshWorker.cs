using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vaulta.Vision.Application;
namespace Vaulta.Vision.Infrastructure;
internal sealed class VisualIndexRefreshWorker(IServiceScopeFactory scopes,VisionOptions options,ILogger<VisualIndexRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(string.IsNullOrWhiteSpace(options.ModelManifestPath)) return;
        while(!stoppingToken.IsCancellationRequested)
        {
            try { await using var scope=scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<IVisualReferenceBuilder>().LoadAsync(stoppingToken); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
            catch(Exception error) { logger.LogWarning("Visual index refresh unavailable: {ErrorType}",error.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(30),stoppingToken);
        }
    }
}
