using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vaulta.Identity.Application;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.Infrastructure;

internal sealed class InMemoryEventBus(IEnumerable<IEventConsumer> consumers, ILogger<InMemoryEventBus> logger) : IEventBus
{
    public async Task Publish(EventEnvelope message, CancellationToken ct)
    {
        foreach (var consumer in consumers) await consumer.Handle(message, ct);
        logger.LogInformation("Dispatched event {EventId} of type {EventType}", message.Id, message.Type);
    }
}
public sealed class OutboxProcessor(IdentityDbContext db, IEventBus bus, IClock clock, ILogger<OutboxProcessor> logger)
{
    public async Task<int> ProcessBatch(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Row locks avoid simultaneous delivery by multiple instances; delivery remains at-least-once.
        var batch = await db.OutboxMessages.FromSqlRaw("""
            SELECT * FROM identity.outbox_messages
            WHERE processed_at IS NULL AND retry_count < 10
              AND (error IS NULL OR occurred_at + (LEAST(300, power(2, retry_count)) * interval '1 second') < now())
            ORDER BY occurred_at LIMIT 20 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);
        foreach (var message in batch)
        {
            try
            {
                await bus.Publish(new EventEnvelope(message.Id, message.Type, message.Payload, message.OccurredAt), ct);
                message.ProcessedAt = clock.UtcNow; message.Error = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                // Store/log the category only: consumer exceptions may contain private payloads.
                message.RetryCount++; message.Error = e.GetType().Name;
                logger.LogWarning("Outbox event {EventId} failed with {ErrorType}, attempt {RetryCount}", message.Id, message.Error, message.RetryCount);
            }
        }
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return batch.Count;
    }
}
internal sealed class OutboxDispatcher(IServiceScopeFactory scopes, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatch(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError("Outbox batch failed with {ErrorType}", e.GetType().Name); }
        }
    }
}
