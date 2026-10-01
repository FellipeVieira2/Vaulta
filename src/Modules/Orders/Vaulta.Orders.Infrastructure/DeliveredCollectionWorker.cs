using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vaulta.Orders.Application;

namespace Vaulta.Orders.Infrastructure;

internal sealed class DeliveredCollectionWorker(IServiceScopeFactory scopes, ILogger<DeliveredCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var offset = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var orders = await scope.ServiceProvider.GetRequiredService<IOrderCollectionStore>().DeliveredOrders(offset, ct);
                foreach (var order in orders)
                {
                    try
                    {
                        await using var transfer = scopes.CreateAsyncScope();
                        await transfer.ServiceProvider.GetRequiredService<IOrderCollection>().TransferDeliveredItem(order, ct);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    { logger.LogWarning("Collection transfer for order {OrderId} will retry.", order.Id); }
                }
                offset = orders.Count < 50 ? 0 : offset + 50;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            { logger.LogWarning("Delivered collection reconciliation will retry."); }
        }
    }
}
