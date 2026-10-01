using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vaulta.Orders.Application;

namespace Vaulta.Orders.Infrastructure;

internal sealed class CancelledListingWorker(IServiceScopeFactory scopes, ILogger<CancelledListingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var offset = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Cycle through all terminal orders in bounded pages. Rechecking
                // protects recovery after a crash or delayed sold-listing write.
                await using var scope = scopes.CreateAsyncScope();
                var orders = await scope.ServiceProvider.GetRequiredService<IOrderStore>().ReleasableOrders(offset, stoppingToken);
                foreach (var order in orders)
                {
                    try
                    {
                        await using var release = scopes.CreateAsyncScope();
                        await release.ServiceProvider.GetRequiredService<IOrderMarketplace>().ReleaseListing(order, stoppingToken);
                    }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogWarning("Listing for order {OrderId} could not be released; will retry.", order.Id); }
                }
                offset = orders.Count < 50 ? 0 : offset + 50;
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Cancelled listing reconciliation could not run; will retry."); }
        }
    }
}
