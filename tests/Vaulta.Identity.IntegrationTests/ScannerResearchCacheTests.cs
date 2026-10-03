using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class ScannerResearchCacheTests(ApiFixture fixture)
{
    [Fact]
    public async Task CachedQuoteCannotUpgradeTheConfidenceOfALessCertainCapture()
    {
        var clock = new Clock(); var boundary = new Provider(clock);
        await using var factory = Factory(boundary, clock); var card = Card();
        Assert.Equal(.92, (await Research(factory, card))!.Estimate!.Confidence);
        var cached = (await Research(factory, card with { Confidence = .71 }))!;
        Assert.Equal(.71, cached.Identification.Confidence);
        Assert.Equal(.71, cached.Estimate!.Confidence);
        Assert.Equal(.71, cached.Estimate.Identification.Confidence);
        Assert.Equal(31m, cached.Estimate.AmountBrl);
        Assert.Equal(1, boundary.Calls);
    }

    [Fact]
    public async Task ConcurrentProcessesReuseOneQuoteAndPreserveEachCaptureSerialWithoutPersistingIt()
    {
        var clock = new Clock(); var boundary = new Provider(clock);
        await using var factory = Factory(boundary, clock);
        var card = Card() with { Certification = new("PSA", "10", "private-first") };
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Research(factory, card with { Certification = card.Certification with { Number = "private-" + i } })));
        Assert.All(results, result => Assert.Equal(31m, result!.Estimate!.AmountBrl));
        for (var i = 0; i < results.Length; i++) Assert.Equal("private-" + i, results[i]!.Estimate!.Identification.Certification!.Number);
        Assert.Equal(1, boundary.Calls);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var payloads = await db.Database.SqlQueryRaw<string>("SELECT payload::text AS \"Value\" FROM catalog.scanner_research_snapshots").ToArrayAsync();
        Assert.All(payloads, payload => Assert.DoesNotContain("private-", payload));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task CacheNeverCrossesFinishLanguageSetOrCertificationGrade()
    {
        var clock = new Clock(); var boundary = new Provider(clock);
        await using var factory = Factory(boundary, clock); var card = Card();
        var identities = new[] { card, card with { Finish = "reverse" }, card with { Language = "en" }, card with { Language = null },
            card with { SetName = "Other set" }, card with { Certification = new("PSA", "10", "one") },
            card with { Certification = new("PSA", "9", "two") }, card with { Certification = new("CGC", "10", "three") },
            card with { SurfaceTreatment = "textured" }, card with { Attributes = new(null, 2025, null, null) } };
        foreach (var identity in identities)
        {
            var result = (await Research(factory, identity))!;
            if (identity.Language is null) Assert.Null(result.Estimate); else Assert.NotNull(result.Estimate);
        }
        foreach (var identity in identities) Assert.Equal(identity.Language, (await Research(factory, identity))!.Identification.Language);
        Assert.Equal(10, boundary.Calls);
    }

    [Fact]
    public async Task TransientMissIsCachedBrieflyThenRetriedWithoutInventingZeroPrice()
    {
        var clock = new Clock(); var boundary = new Provider(clock) { Miss = true };
        await using var factory = Factory(boundary, clock); var card = Card();
        Assert.Null((await Research(factory, card))!.Estimate);
        Assert.Null((await Research(factory, card))!.Estimate);
        Assert.Equal(1, boundary.Calls);
        boundary.Miss = false; clock.UtcNow = clock.UtcNow.AddMinutes(3);
        Assert.Equal(31m, (await Research(factory, card))!.Estimate!.AmountBrl);
        Assert.Equal(2, boundary.Calls);
    }

    [Fact]
    public async Task CorrectedNumberIsStoredOnlyUnderCorrectedKey()
    {
        var clock = new Clock(); var boundary = new Provider(clock) { CorrectNumber = true };
        await using var factory = Factory(boundary, clock); var card = Card() with { CollectorNumber = "062/066" };
        Assert.Equal("067/086", (await Research(factory, card))!.Identification.CollectorNumber);
        Assert.Equal("067/086", (await Research(factory, card with { CollectorNumber = "067/086" }))!.Identification.CollectorNumber);
        Assert.Equal(1, boundary.Calls);
        await Research(factory, card);
        Assert.Equal(2, boundary.Calls);
    }

    [Fact]
    public async Task PositiveCacheExpiresAtBrasiliaFiveAndDailyBatchesContinueWithoutImages()
    {
        var clock = new Clock { UtcNow = DateTimeOffset.Parse("2026-10-03T07:59:00Z") }; var boundary = new Provider(clock);
        await using var factory = Factory(boundary, clock); var first = Card(); var second = Card();
        var quote = (await Research(factory, first))!.Estimate!;
        await Research(factory, second);
        Assert.Equal(DateTimeOffset.Parse("2026-10-03T08:00:00Z"), quote.NextRefreshAt);
        Assert.Equal(clock.UtcNow, (await Research(factory, first))!.Estimate!.CheckedAt);
        clock.UtcNow = DateTimeOffset.Parse("2026-10-03T08:00:00Z");
        var job = new DailyMarketPriceRefreshJob(factory.Services.GetRequiredService<IServiceScopeFactory>(), clock,
            Options.Create(new MarketPriceRefreshOptions { MaxPrintingsPerRun = 1 }), NullLogger<DailyMarketPriceRefreshJob>.Instance);
        // Small batches must leave the next due identity selectable in the same market day.
        await job.RunAsync(default); await job.RunAsync(default);
        Assert.Equal(4, boundary.Calls);
        Assert.Equal(2, boundary.NoImageCalls);
        Assert.Equal(clock.UtcNow, (await Research(factory, first))!.Estimate!.CheckedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T08:00:00Z"), (await Research(factory, second))!.Estimate!.NextRefreshAt);
    }

    [Fact]
    public async Task CallerCancellationDoesNotBecomeNegativeCache()
    {
        var clock = new Clock(); var boundary = new Provider(clock) { Block = true };
        await using var factory = Factory(boundary, clock); var card = Card();
        using var cancellation = new CancellationTokenSource();
        var pending = Research(factory, card, cancellation.Token);
        await boundary.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        boundary.Block = false;
        Assert.Equal(31m, (await Research(factory, card))!.Estimate!.AmountBrl);
        Assert.Equal(2, boundary.Calls);
    }

    private WebApplicationFactory<Program> Factory(Provider boundary, Clock clock) => fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IScannerWebMarketResearchProvider>(); services.AddSingleton<IScannerWebMarketResearchProvider>(boundary);
        services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock);
    }));
    private static CardVisualIdentificationDto Card() => new("Cache " + Guid.NewGuid().ToString("N"), "067/086", "pt-BR", "Set", .92, "pokemon", Finish: "normal");
    private static async Task<ScannerMarketResearchResultDto?> Research(WebApplicationFactory<Program> factory, CardVisualIdentificationDto card, CancellationToken ct = default)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IScannerMarketResearch>().ResearchAsync(card, [1], ct);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-10-03T10:00:00Z"); }
    private sealed class Provider(Clock clock) : IScannerWebMarketResearchProvider
    {
        public int Calls; public int NoImageCalls; public bool Miss; public bool CorrectNumber; public bool Block;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ScannerMarketResearchResultDto?> ResearchAsync(CardVisualIdentificationDto card, byte[]? image, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls); if (image is null) Interlocked.Increment(ref NoImageCalls);
            Started.TrySetResult();
            if (Block) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            await Task.Delay(30, ct);
            if (Miss) return null;
            if (CorrectNumber) card = card with { CollectorNumber = "067/086" };
            return new(card, new(31m, "Web", clock.UtcNow, .92, card,
                [new("https://market.example/card", "Card", 31m, "BRL", "asking", clock.UtcNow)]));
        }
    }
}
