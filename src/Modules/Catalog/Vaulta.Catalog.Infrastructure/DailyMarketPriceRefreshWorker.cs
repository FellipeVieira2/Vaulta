using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;
using Vaulta.SharedKernel;

namespace Vaulta.Catalog.Infrastructure;

internal sealed class DailyMarketPriceRefreshWorker(DailyMarketPriceRefreshJob job, IClock clock,
    IOptions<MarketPriceRefreshOptions> options, ILogger<DailyMarketPriceRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        DateOnly? attemptedDay = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var day = MarketPriceDay.At(clock.UtcNow).Date;
            if (attemptedDay != day)
            {
                attemptedDay = day;
                try { await job.RunAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { logger.LogWarning("Daily market refresh interrupted: {ErrorType}", ex.GetType().Name); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
