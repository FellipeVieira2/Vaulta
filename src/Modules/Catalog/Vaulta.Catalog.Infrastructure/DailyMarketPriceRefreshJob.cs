using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;
using Vaulta.SharedKernel;
using System.Text.Json;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Infrastructure;

public sealed class MarketPriceRefreshOptions
{
    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 300;
    public int MaxPrintingsPerRun { get; set; } = 20_000;
    public int MaxConcurrency { get; set; } = 2;
    public int MaxRunMinutes { get; set; } = 120;
    public bool IsValid() => PollIntervalSeconds is >= 60 and <= 3600 && MaxPrintingsPerRun is >= 1 and <= 20_000
        && MaxConcurrency is >= 1 and <= 2 && MaxRunMinutes is >= 1 and <= 240;
}

// Refresh only canonical printings and researched identities previously consulted, using the shared daily
// cache/lock. It never invokes vision or reconstructs identity from photographs.
public sealed class DailyMarketPriceRefreshJob(IServiceScopeFactory scopes, IClock clock,
    IOptions<MarketPriceRefreshOptions> options, ILogger<DailyMarketPriceRefreshJob> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var limits = options.Value;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(limits.MaxRunMinutes));
        var day = MarketPriceDay.At(clock.UtcNow);
        Guid[] ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var now = clock.UtcNow;
            // Receipts are short-lived request artifacts; they never enter physical-price refresh.
            await db.ScannerResearchSnapshots.Where(x => x.Outcome == MarketResearch.ScannerMarketResearch.CaptureReceiptOutcome && x.RefreshAfter <= now)
                .ExecuteDeleteAsync(deadline.Token);
            ids = await db.Printings.AsNoTracking().Where(p => p.IsActive
                    && db.DailyMarketSnapshots.Any(x => x.PrintingId == p.Id && x.MarketDay < day.Date)
                    && !db.DailyMarketSnapshots.Any(x => x.PrintingId == p.Id && x.MarketDay == day.Date))
                .OrderBy(p => p.Id).Select(p => p.Id).Take(limits.MaxPrintingsPerRun).ToArrayAsync(deadline.Token);
        }
        var refreshed = 0;
        await Parallel.ForEachAsync(ids, new ParallelOptions { MaxDegreeOfParallelism = limits.MaxConcurrency, CancellationToken = deadline.Token },
            async (id, ct) =>
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<IScannerCardDetailsReader>().GetAsync(id, ct);
                    if (result?.FetchedAt is not null && result.NextRefreshAt > clock.UtcNow) Interlocked.Increment(ref refreshed);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogWarning("Daily market refresh failed for {PrintingId}: {ErrorType}", id, ex.GetType().Name); }
            });
        string[] researched;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var now = clock.UtcNow;
            researched = await db.ScannerResearchSnapshots.AsNoTracking().Where(x => x.RefreshAfter <= now && x.MarketDay < day.Date
                    && (x.Outcome == "quoted" || x.Outcome == "identity_only"))
                .OrderBy(x => x.RefreshAfter).ThenBy(x => x.CacheKey).Select(x => x.Payload).Take(limits.MaxPrintingsPerRun).ToArrayAsync(deadline.Token);
        }
        await Parallel.ForEachAsync(researched, new ParallelOptions { MaxDegreeOfParallelism = limits.MaxConcurrency, CancellationToken = deadline.Token },
            async (payload, ct) =>
            {
                try
                {
                    var identity = JsonSerializer.Deserialize<ScannerMarketResearchResultDto>(payload)?.Identification;
                    if (identity is null) return;
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<IScannerMarketResearch>().ResearchAsync(identity, null, ct);
                    if (result?.Estimate is not null) Interlocked.Increment(ref refreshed);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogWarning("Daily researched market refresh failed: {ErrorType}", ex.GetType().Name); }
            });
        logger.LogInformation("Daily market refresh completed: {Refreshed} of {Requested} identities", refreshed, ids.Length + researched.Length);
        return refreshed;
    }
}
