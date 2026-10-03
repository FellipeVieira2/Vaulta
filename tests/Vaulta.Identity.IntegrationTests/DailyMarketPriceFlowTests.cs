using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Identity.Contracts;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class DailyMarketPriceFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task DailyRefreshJobUpdatesKnownCardWithoutAUserRequestOrNewVisionCall()
    {
        var provider = new Provider(); var handler = new CardHandler(provider.CardId);
        var clock = new Clock(); var rates = new Rates();
        await using var database = await TestPostgresDatabase.Start();
        await using var factory = CreateFactory(handler, clock, rates).WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Vaulta"] = database.ConnectionString })));
        var printing = await Seed(factory.Services, provider);
        using var client = factory.CreateClient(); await Authenticate(client);
        Assert.Equal(60m, Assert.Single((await Get(client, printing)).MarketQuotes).MarketValueBrl);
        var job = new DailyMarketPriceRefreshJob(factory.Services.GetRequiredService<IServiceScopeFactory>(), clock,
            Options.Create(new MarketPriceRefreshOptions { Enabled = true }), NullLogger<DailyMarketPriceRefreshJob>.Instance);
        clock.UtcNow = DateTimeOffset.Parse("2026-10-02T07:59:59Z");
        Assert.Equal(0, await job.RunAsync(default));
        handler.Price = 20;
        clock.UtcNow = DateTimeOffset.Parse("2026-10-02T08:00:00Z");
        Assert.True(await job.RunAsync(default) >= 1);
        await using var scope = factory.Services.CreateAsyncScope();
        var snapshot = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().DailyMarketSnapshots.AsNoTracking()
            .SingleAsync(x => x.PrintingId == printing && x.MarketDay == new DateOnly(2026, 10, 2));
        Assert.Equal(120m, Assert.Single(JsonSerializer.Deserialize<ScannerCardDetailsDto>(snapshot.Payload)!.MarketQuotes).MarketValueBrl);
        Assert.Equal(0, await job.RunAsync(default));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task DailyBrlSnapshotSurvivesNewScopesAndRefreshesOnceForConcurrentRequestsAfterFiveAm()
    {
        var provider = new Provider();
        var handler = new CardHandler(provider.CardId);
        var clock = new Clock();
        var rates = new Rates();
        await using var factory = CreateFactory(handler, clock, rates);
        var printing = await Seed(factory.Services, provider);
        using var client = factory.CreateClient();
        await Authenticate(client);
        var first = await Get(client, printing);
        Assert.Equal(60m, Assert.Single(first.MarketQuotes).MarketValueBrl);
        Assert.Equal(DateTimeOffset.Parse("2026-10-02T08:00:00Z"), first.NextRefreshAt);
        handler.Price = 20; rates.Value = 7;
        clock.UtcNow = DateTimeOffset.Parse("2026-10-02T07:59:59Z");
        // Every HTTP request has a fresh DbContext; no process-memory card cache.
        await Authenticate(client);
        var beforeCutoff = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Get(client, printing)));
        Assert.All(beforeCutoff, x => Assert.Equal(60m, Assert.Single(x.MarketQuotes).MarketValueBrl));
        Assert.Equal(1, handler.Calls); Assert.Equal(1, rates.Calls);

        clock.UtcNow = DateTimeOffset.Parse("2026-10-02T08:00:00Z");
        var gate = handler.BlockNext();
        var concurrent = Enumerable.Range(0, 8).Select(_ => Get(client, printing)).ToArray();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        gate.Release.TrySetResult();
        var refreshed = await Task.WhenAll(concurrent);
        Assert.All(refreshed, x => Assert.Equal(140m, Assert.Single(x.MarketQuotes).MarketValueBrl));
        Assert.Equal(2, handler.Calls); Assert.Equal(2, rates.Calls);
        Assert.Equal(DateTimeOffset.Parse("2026-10-03T08:00:00Z"), refreshed[0].NextRefreshAt);

        // A fresh host also reuses the database snapshot, including its FX rate.
        await using var restarted = CreateFactory(handler, clock, rates);
        using var restartedClient = restarted.CreateClient();
        await Authenticate(restartedClient);
        rates.Value = 9;
        Assert.Equal(140m, Assert.Single((await Get(restartedClient, printing)).MarketQuotes).MarketValueBrl);
        Assert.Equal(2, handler.Calls);

        clock.UtcNow = DateTimeOffset.Parse("2026-10-05T10:00:00Z");
        await Authenticate(restartedClient);
        Assert.Equal(180m, Assert.Single((await Get(restartedClient, printing)).MarketQuotes).MarketValueBrl);
        await using var scope = restarted.Services.CreateAsyncScope();
        var snapshots = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().DailyMarketSnapshots
            .AsNoTracking().Where(x => x.PrintingId == printing).OrderBy(x => x.MarketDay).ToListAsync();
        Assert.Equal(new[] { new DateOnly(2026, 10, 1), new(2026, 10, 2), new(2026, 10, 5) }, snapshots.Select(x => x.MarketDay));
        Assert.Equal(60m, Assert.Single(JsonSerializer.Deserialize<ScannerCardDetailsDto>(snapshots[0].Payload)!.MarketQuotes).MarketValueBrl);
        Assert.Equal(140m, Assert.Single(JsonSerializer.Deserialize<ScannerCardDetailsDto>(snapshots[1].Payload)!.MarketQuotes).MarketValueBrl);
        Assert.Equal(3, handler.Calls);
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task ProviderAndFxFailuresDoNotPersistAnEmptyDayOrPresentOldPricesAsFresh()
    {
        var provider = new Provider(); var handler = new CardHandler(provider.CardId);
        var clock = new Clock(); var rates = new Rates();
        await using var factory = CreateFactory(handler, clock, rates);
        var printing = await Seed(factory.Services, provider);
        using var client = factory.CreateClient(); await Authenticate(client);
        Assert.Single((await Get(client, printing)).MarketQuotes);
        clock.UtcNow = DateTimeOffset.Parse("2026-10-02T10:00:00Z");
        await Authenticate(client);
        handler.Status = HttpStatusCode.ServiceUnavailable;
        var unavailable = await Get(client, printing);
        Assert.Empty(unavailable.MarketQuotes); Assert.Null(unavailable.FetchedAt);
        handler.Status = HttpStatusCode.OK; handler.WrongIdentity = true;
        Assert.Empty((await Get(client, printing)).MarketQuotes);
        handler.WrongIdentity = false; rates.Value = null;
        unavailable = await Get(client, printing);
        Assert.Empty(unavailable.MarketQuotes); Assert.Null(unavailable.NextRefreshAt);
        Assert.NotEmpty(unavailable.Information);
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().DailyMarketSnapshots.CountAsync(x => x.PrintingId == printing));
        rates.Value = 8;
        Assert.Equal(80m, Assert.Single((await Get(client, printing)).MarketQuotes).MarketValueBrl);
        Assert.Equal(5, handler.Calls);
        await using var finalScope = factory.Services.CreateAsyncScope();
        Assert.Equal(2, await finalScope.ServiceProvider.GetRequiredService<CatalogDbContext>().DailyMarketSnapshots.CountAsync(x => x.PrintingId == printing));
    }

    [Fact]
    public async Task ValidCardWithoutMarketQuoteIsCachedWithoutInventingZeroOrRequestingExchangeRates()
    {
        var provider = new Provider(); var handler = new CardHandler(provider.CardId) { HasPrice = false };
        var clock = new Clock(); var rates = new Rates();
        await using var factory = CreateFactory(handler, clock, rates);
        var printing = await Seed(factory.Services, provider);
        using var client = factory.CreateClient(); await Authenticate(client);
        var details = await Get(client, printing);
        Assert.Empty(details.MarketQuotes); Assert.NotNull(details.FetchedAt);
        Assert.NotEmpty(details.Information);
        handler.HasPrice = true;
        Assert.Empty((await Get(client, printing)).MarketQuotes);
        Assert.Equal(1, handler.Calls); Assert.Equal(0, rates.Calls);
        clock.UtcNow = DateTimeOffset.Parse("2026-10-02T10:00:00Z");
        await Authenticate(client);
        Assert.Single((await Get(client, printing)).MarketQuotes);
        Assert.Equal(2, handler.Calls); Assert.Equal(1, rates.Calls);
    }

    [Fact]
    public async Task CancelledRefreshRollsBackAndReleasesThePrintingLock()
    {
        var provider = new Provider(); var handler = new CardHandler(provider.CardId);
        var clock = new Clock(); var rates = new Rates();
        await using var factory = CreateFactory(handler, clock, rates);
        var printing = await Seed(factory.Services, provider);
        var gate = handler.BlockNext();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        // Use the scoped reader so cancellation is exercised through EF/HTTP,
        // rather than merely cancelling the test client's response wait.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var pending = scope.ServiceProvider.GetRequiredService<IScannerCardDetailsReader>().GetAsync(printing, cancellation.Token);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        await using var nextScope = factory.Services.CreateAsyncScope();
        Assert.Equal(0, await nextScope.ServiceProvider.GetRequiredService<CatalogDbContext>().DailyMarketSnapshots.CountAsync(x => x.PrintingId == printing));
        var result = await nextScope.ServiceProvider.GetRequiredService<IScannerCardDetailsReader>().GetAsync(printing, default).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(result!.MarketQuotes); Assert.Equal(2, handler.Calls);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory(CardHandler handler, Clock clock, Rates rates) =>
        fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IScannerCardDetailsReader>();
            services.RemoveAll<TcgDexScannerDetailsReader>();
            services.AddScoped<TcgDexScannerDetailsReader>(sp => new TcgDexScannerDetailsReader(
                new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://cards.example.test/") },
                sp.GetRequiredService<CatalogDbContext>(), sp.GetRequiredService<ICatalogSearch>(), rates, clock,
                NullLogger<TcgDexScannerDetailsReader>.Instance));
            services.AddScoped<IScannerCardDetailsReader>(sp=>sp.GetRequiredService<TcgDexScannerDetailsReader>());
        }));

    private static async Task<Guid> Seed(IServiceProvider services, Provider provider)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await new CatalogSyncService(db, [provider], scope.ServiceProvider.GetRequiredService<IClock>(), NullLogger<CatalogSyncService>.Instance)
            .Synchronize(provider.Code, provider.SetId, default);
        return await db.ExternalIds.Where(x => x.Provider == provider.Code && x.EntityType == "printing" && x.ExternalId == provider.CardId).Select(x => x.EntityId).SingleAsync();
    }

    private static async Task Authenticate(HttpClient client)
    {
        var request = new RegisterRequest($"{Guid.NewGuid():N}@example.test", "Secure-Test-Password1!", "u" + Guid.NewGuid().ToString("N")[..20], "Collector");
        (await client.PostAsJsonAsync("/api/v1/auth/register", request)).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken);
    }

    private static async Task<ScannerCardDetailsDto> Get(HttpClient client, Guid printing)
    {
        using var response = await client.GetAsync($"/api/v1/scanner/printings/{printing}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ScannerCardDetailsDto>())!;
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-10-01T13:00:00Z");
    }
    private sealed class Rates : IBrlExchangeRateProvider
    {
        private int _calls;
        public int Calls => _calls;
        public decimal? Value { get; set; } = 6;
        public Task<BrlExchangeRate?> GetAsync(string currency, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(Value is { } rate ? new BrlExchangeRate(currency, rate, DateTimeOffset.Parse("2026-10-01T12:00:00Z")) : null);
        }
    }
    private sealed class CardHandler(string externalId) : HttpMessageHandler
    {
        private int _calls;
        private Gate? _next;
        public int Calls => _calls;
        public decimal Price { get; set; } = 10;
        public bool HasPrice { get; set; } = true;
        public bool WrongIdentity { get; set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Gate BlockNext() => _next = new Gate();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            Assert.EndsWith("/en/cards/" + externalId, request.RequestUri!.AbsolutePath);
            var gate = Interlocked.Exchange(ref _next, null);
            if (gate is not null) { gate.Entered.TrySetResult(); await gate.Release.Task.WaitAsync(ct); }
            return new(Status)
            {
                Content = JsonContent.Create(new
                {
                    id = WrongIdentity ? "wrong-card" : externalId, name = "Daily test card", hp = 60,
                    pricing = HasPrice ? new { cardmarket = new { unit = "EUR", updated = "2026-10-01T12:00:00Z", trend = Price, avg7 = 8m } } : null
                })
            };
        }
    }
    private sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Provider : ICatalogProvider
    {
        private readonly string _key = Guid.NewGuid().ToString("N");
        public string Code => "tcgdex";
        public string SetId => "daily-" + _key;
        public string CardId => "card-" + _key;
        private ProviderSet Set => new(SetId, "Daily price test " + _key, null, new(2026, 1, 1));
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken ct) => Task.FromResult<IReadOnlyList<ProviderSet>>([Set]);
        public Task<ProviderSetDetails> GetSetDetails(string setId, CancellationToken ct) => Task.FromResult(new ProviderSetDetails(Set,
            [new(CardId, "Daily price test " + _key, "1", "en", "Rare", null, [new("normal", "Normal", "normal")])]));
    }
}
