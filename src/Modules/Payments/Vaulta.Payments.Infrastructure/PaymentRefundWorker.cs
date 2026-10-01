using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vaulta.Payments.Application;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Infrastructure;

internal sealed class PaymentRefundWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<PaymentRefundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                IReadOnlyList<Guid> ids;
                await using (var scope = scopes.CreateAsyncScope())
                    ids = await scope.ServiceProvider.GetRequiredService<IRefundStore>()
                        .Due(scope.ServiceProvider.GetRequiredService<IClock>().UtcNow, stoppingToken);
                foreach (var id in ids)
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<PaymentRefundService>()
                            .Process(id, configuration.GetValue("Refunds:RequestsEnabled", false), stoppingToken);
                    }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogWarning("Refund for payment {PaymentId} requires reconciliation.", id); }
                }
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Refund requests could not be refreshed; will retry."); }
        }
    }
}
