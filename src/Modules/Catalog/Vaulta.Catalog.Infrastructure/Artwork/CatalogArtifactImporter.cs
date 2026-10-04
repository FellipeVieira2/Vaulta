using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using Vaulta.Assets.Application;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.SharedKernel;

namespace Vaulta.Catalog.Infrastructure.Artwork;

// Finite operator ingestion. No camera/request endpoint invokes this class.
public sealed class CatalogArtifactImporter(
    IServiceScopeFactory scopeFactory,
    CatalogArtworkDownloader download,
    ICatalogSearch catalog,
    IBrlExchangeRateProvider exchange,
    IClock clock,
    IOptions<ArtworkImportOptions> options,
    ILogger<CatalogArtifactImporter> logger) : ICatalogArtifactImporter
{
    private readonly ArtworkImportOptions _options = options.Value;

    public async Task<CatalogArtifactImportReport> ImportAsync(Guid? setId, CancellationToken ct)
    {
        // Serialize content-addressed asset upserts across operator processes.
        // Advisory lock acquired on a short-lived scope so workers use independent connections.
        await using var lockScope = scopeFactory.CreateAsyncScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await lockDb.Database.OpenConnectionAsync(ct);
        var key = BitConverter.ToInt64(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("vaulta:catalog:artifacts")), 0);
        var connection = lockDb.Database.GetDbConnection();
        await using var acquire = connection.CreateCommand();
        acquire.CommandText = "SELECT pg_try_advisory_lock(@key)";
        var parameter = acquire.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = key;
        acquire.Parameters.Add(parameter);
        var locked = (bool)(await acquire.ExecuteScalarAsync(ct) ?? false);
        if (!locked)
        {
            await lockDb.Database.CloseConnectionAsync();
            throw new InvalidOperationException("Catalog artifact import already running.");
        }

        try
        {
            // Load FX rates once (shared read-only data)
            var rates = new Dictionary<string, BrlExchangeRate>();
            foreach (var currency in new[] { "USD", "EUR" })
            {
                try
                {
                    var rate = await exchange.GetAsync(currency, ct);
                    if (rate is not null) rates[currency] = rate;
                }
                catch (Exception error) when (error is HttpRequestException or JsonException)
                {
                    logger.LogWarning("FX unavailable: {Currency} {ErrorType}", currency, error.GetType().Name);
                }
            }

            // Count total candidates for progress reporting
            int totalCandidates;
            {
                await using var countScope = scopeFactory.CreateAsyncScope();
                var countDb = countScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                totalCandidates = await countDb.Printings
                    .Where(x => x.IsActive && (setId == null || x.SetId == setId))
                    .CountAsync(ct);
            }

            // Producer-Consumer with bounded channel
            var channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(_options.WorkerCount * 2)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true
            });

            // Thread-safe counters (class fields, no ref needed in async methods)
            var counters = new ImportCounters { LastLogTicks = clock.UtcNow.UtcTicks };
            var startTime = clock.UtcNow;

            // Start workers
            var workers = new Task[_options.WorkerCount];
            for (var i = 0; i < _options.WorkerCount; i++)
            {
                workers[i] = Task.Run(() => WorkerLoopAsync(
                    channel.Reader, setId, rates, counters, totalCandidates, startTime, ct));
            }

            // Producer: paginate and enqueue IDs only (no entity tracking on producer scope)
            Guid? after = null;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                await using var pageScope = scopeFactory.CreateAsyncScope();
                var pageDb = pageScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var page = await pageDb.Printings
                    .AsNoTracking()
                    .Where(x => x.IsActive && (setId == null || x.SetId == setId) &&
                                (after == null || x.Id.CompareTo(after.Value) > 0))
                    .OrderBy(x => x.Id)
                    .Select(x => x.Id)
                    .Take(_options.PageSize)
                    .ToArrayAsync(ct);
                if (page.Length == 0) break;
                foreach (var printingId in page)
                    await channel.Writer.WriteAsync(printingId, ct);
                after = page[^1];
            }

            channel.Writer.Complete();
            await Task.WhenAll(workers);

            return new CatalogArtifactImportReport(
                (int)Interlocked.Read(ref counters.Ready),
                (int)Interlocked.Read(ref counters.Unchanged),
                (int)Interlocked.Read(ref counters.Missing),
                (int)Interlocked.Read(ref counters.Failed),
                (int)Interlocked.Read(ref counters.Priced));
        }
        finally
        {
            await using var release = connection.CreateCommand();
            release.CommandText = "SELECT pg_advisory_unlock(@key)";
            var argument = release.CreateParameter();
            argument.ParameterName = "key";
            argument.Value = key;
            release.Parameters.Add(argument);
            try { await release.ExecuteNonQueryAsync(CancellationToken.None); }
            finally { await lockDb.Database.CloseConnectionAsync(); }
        }
    }

    private async Task WorkerLoopAsync(
        ChannelReader<Guid> reader,
        Guid? setId,
        Dictionary<string, BrlExchangeRate> rates,
        ImportCounters counters,
        int totalCandidates, DateTimeOffset startTime,
        CancellationToken ct)
    {
        await foreach (var printingId in reader.ReadAllAsync(ct))
        {
            ct.ThrowIfCancellationRequested();

            // Each iteration gets its own scope with isolated DbContexts
            await using var scope = scopeFactory.CreateAsyncScope();
            var workerDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var workerAssets = scope.ServiceProvider.GetRequiredService<ISystemAssetService>();
            var workerClock = scope.ServiceProvider.GetRequiredService<IClock>();

            try
            {
                var printing = await workerDb.Printings
                    .Include(x => x.Variants)
                    .SingleOrDefaultAsync(x => x.Id == printingId, ct);
                if (printing is null) continue;

                var recent = printing.ArtworkImportStatus == "ready"
                             && printing.ArtworkAssetId is not null
                             && printing.ThumbnailAssetId is not null
                             && printing.ArtworkSha256 is not null
                             && printing.ArtworkCheckedAt is { } checkedAt
                             && checkedAt + TimeSpan.FromHours(_options.RevalidateAfterHours) > workerClock.UtcNow;

                if (recent)
                {
                    Interlocked.Increment(ref counters.Unchanged);
                }
                else if (printing.ExternalArtworkUrl is null)
                {
                    printing.ArtworkImportStatus = "missing";
                    Interlocked.Increment(ref counters.Missing);
                }
                else
                {
                    var result = await download.DownloadAsync(
                        printing.ExternalArtworkUrl,
                        printing.ArtworkAssetId is null ? null : printing.ArtworkETag,
                        printing.ArtworkAssetId is null ? null : printing.ArtworkLastModified,
                        ct);

                    if (result.Status == "not_modified" && printing.ArtworkAssetId is null)
                        throw new InvalidDataException("304 without stored artwork.");

                    if (result.Status == "not_modified"
                        || (result.Status == "ready" && result.SourceSha256 == printing.ArtworkSha256 && printing.ArtworkAssetId is not null))
                    {
                        printing.ArtworkImportStatus = "ready";
                        Interlocked.Increment(ref counters.Unchanged);
                    }
                    else if (result.Status == "missing")
                    {
                        printing.ArtworkImportStatus = "missing";
                        Interlocked.Increment(ref counters.Missing);
                    }
                    else
                    {
                        var imageInfo = Image.Identify(result.Image!);
                        var thumbInfo = Image.Identify(result.Thumbnail!);
                        printing.ArtworkAssetId = await workerAssets.StoreArtworkAsync(
                            result.Image!, imageInfo.Width, imageInfo.Height,
                            printing.ExternalArtworkUrl, false, ct);
                        printing.ThumbnailAssetId = await workerAssets.StoreArtworkAsync(
                            result.Thumbnail!, thumbInfo.Width, thumbInfo.Height,
                            printing.ExternalArtworkUrl, true, ct);
                        printing.ArtworkSha256 = result.SourceSha256;
                        printing.ArtworkImportStatus = "ready";
                        Interlocked.Increment(ref counters.Ready);
                    }

                    if (result.Status == "ready")
                    {
                        printing.ArtworkETag = result.ETag;
                        printing.ArtworkLastModified = result.LastModified;
                    }
                }

                printing.ArtworkImportError = null;

                // Pricing post-upload: failure does not block artwork ready status
                if (printing.SourcePricingJson is not null
                    || printing.Variants.Any(x => x.RawValue?.TrimStart().StartsWith('{') == true))
                {
                    try
                    {
                        var details = await catalog.GetPrinting(printing.Id, ct);
                        var quotes = CatalogPriceParser.Read(
                            printing.SourcePricingJson, printing.Variants.ToArray(), rates);
                        if (quotes.Count > 0)
                        {
                            var day = MarketPriceDay.At(workerClock.UtcNow);
                            var snapshot = await workerDb.DailyMarketSnapshots
                                .SingleOrDefaultAsync(x => x.PrintingId == printing.Id && x.MarketDay == day.Date, ct);
                            if (snapshot is null)
                            {
                                snapshot = new() { PrintingId = printing.Id, MarketDay = day.Date };
                                workerDb.DailyMarketSnapshots.Add(snapshot);
                            }
                            snapshot.FetchedAt = workerClock.UtcNow;
                            snapshot.RefreshAfter = day.RefreshAfter;
                            snapshot.Payload = JsonSerializer.Serialize(new ScannerCardDetailsDto(
                                details!, new Dictionary<string, string>(), quotes,
                                "Referência internacional convertida pela PTAX, importada para o catálogo local.",
                                workerClock.UtcNow, day.RefreshAfter));
                            Interlocked.Increment(ref counters.Priced);
                        }
                    }
                    catch (Exception pricingError) when (pricingError is not OperationCanceledException)
                    {
                        logger.LogWarning("Pricing failed for {PrintingId}: {ErrorType}",
                            printing.Id, pricingError.GetType().Name);
                    }
                }

                if (!recent) printing.ArtworkCheckedAt = workerClock.UtcNow;
                await workerDb.SaveChangesAsync(ct);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Interlocked.Increment(ref counters.Failed);
                logger.LogWarning("Catalog artifact failed for {PrintingId}: {ErrorType}",
                    printingId, error.GetType().Name);

                // Best-effort failure mark with fresh scope to avoid polluting current tracker
                try
                {
                    await using var failScope = scopeFactory.CreateAsyncScope();
                    var failDb = failScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                    var failClock = failScope.ServiceProvider.GetRequiredService<IClock>();
                    var failPrinting = await failDb.Printings.FindAsync([printingId], ct);
                    if (failPrinting is not null)
                    {
                        failPrinting.ArtworkImportStatus = "failed";
                        failPrinting.ArtworkImportError = error.GetType().Name;
                        failPrinting.ArtworkCheckedAt = failClock.UtcNow;
                        await failDb.SaveChangesAsync(ct);
                    }
                }
                catch { /* best-effort */ }
            }

            // Progress logging: every 100 items OR 30 seconds
            var currentProcessed = Interlocked.Increment(ref counters.Processed);
            var now = clock.UtcNow;
            var lastTicks = Volatile.Read(ref counters.LastLogTicks);
            var elapsedSinceLog = new DateTimeOffset(now.UtcTicks, TimeSpan.Zero) - new DateTimeOffset(lastTicks, TimeSpan.Zero);
            if (currentProcessed % 100 == 0 || elapsedSinceLog.TotalSeconds >= 30)
            {
                if (Interlocked.CompareExchange(ref counters.LastLogTicks, now.UtcTicks, lastTicks) == lastTicks)
                {
                    var elapsed = now - startTime;
                    var throughput = elapsed.TotalSeconds > 0 ? currentProcessed / elapsed.TotalSeconds : 0;
                    logger.LogInformation(
                        "Catalog artwork import | Processed: {Processed:N0}/{Total:N0} | Ready: {Ready:N0} | Skipped: {Skipped:N0} | Missing: {Missing:N0} | Failed: {Failed:N0} | Workers: {Workers} | Throughput: {Throughput:F1} cards/s | Elapsed: {Elapsed}",
                        currentProcessed, totalCandidates,
                        Interlocked.Read(ref counters.Ready), Interlocked.Read(ref counters.Unchanged),
                        Interlocked.Read(ref counters.Missing), Interlocked.Read(ref counters.Failed),
                        _options.WorkerCount, throughput, elapsed);
                }
            }
        }
    }

    private sealed class ImportCounters
    {
        public long Ready;
        public long Unchanged;
        public long Missing;
        public long Failed;
        public long Priced;
        public long Processed;
        public long LastLogTicks;
    }
}