using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vaulta.Payments.Application;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Infrastructure;

internal sealed class SellerPayoutWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<SellerPayoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ledger updates do not move funds. Actual transfers require explicit
        // operational enablement after provider credentials and webhook setup.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                IReadOnlyList<Guid> ids;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<SellerPayoutService>().RecordMissing(stoppingToken);
                    ids = await scope.ServiceProvider.GetRequiredService<IPayoutStore>()
                        .Due(scope.ServiceProvider.GetRequiredService<IClock>().UtcNow, stoppingToken);
                }
                foreach (var id in ids)
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<SellerPayoutService>()
                            .Process(id, configuration.GetValue("Payouts:TransfersEnabled", false), stoppingToken);
                    }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogWarning("Payout {PayoutId} requires another reconciliation check.", id);
                    }
                }
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Seller payout records could not be refreshed; will retry.");
            }
        }
    }
}
