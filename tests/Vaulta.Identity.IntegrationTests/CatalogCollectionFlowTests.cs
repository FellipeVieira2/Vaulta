using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Collection.Contracts;
using Vaulta.Collection.Infrastructure;
using Vaulta.Identity.Contracts;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class CatalogCollectionFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task SyncSearchPrintingVariantAndCollectionWorkEndToEndAndKeepIdentityOnRefresh()
    {
        var provider = new FixtureProvider();
        Guid printingId; Guid cardId; Guid variantId; Guid reverseId; Guid setId;
        Guid[] externalIds;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var sync = Sync(scope.ServiceProvider, provider);
            await sync.Synchronize("fake", provider.SetId, default);
            var printing = await db.Printings.Include(x => x.Variants).SingleAsync(x => x.Set.Name == provider.SetName);
            printingId = printing.Id; cardId = printing.CardId; setId = printing.SetId;
            variantId = printing.Variants.Single(x => x.Code == "normal").Id;
            reverseId = printing.Variants.Single(x => x.Code == "reverse").Id;
            externalIds = await db.ExternalIds.Where(x => x.EntityId == printingId || x.EntityId == cardId || x.EntityId == setId).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            await sync.Synchronize("fake", provider.SetId, default);
            Assert.Single(await db.Sets.Where(x => x.Name == provider.SetName).ToArrayAsync());
            Assert.Single(await db.Cards.Where(x => x.Name == provider.CardName).ToArrayAsync());
            Assert.Single(await db.Printings.Where(x => x.SetId == setId).ToArrayAsync());
            Assert.Equal(2, await db.Variants.CountAsync(x => x.PrintingId == printingId));
            Assert.Equal(externalIds, await db.ExternalIds.Where(x => x.EntityId == printingId || x.EntityId == cardId || x.EntityId == setId).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
            Assert.All(await db.ExternalIds.Where(x => externalIds.Contains(x.Id)).ToArrayAsync(), x => Assert.Equal("fake", x.Provider));
            Assert.Equal("fake", printing.ArtworkProvider);
        }
        using var client = fixture.Factory.CreateClient();
        var page = await client.GetFromJsonAsync<CatalogSearchPage>($"/api/v1/catalog/search?q={Uri.EscapeDataString(provider.CardName)}&game=pokemon&page=1&pageSize=1");
        var found = Assert.Single(page!.Items);
        Assert.Equal(1, page.TotalCount); Assert.Equal(1, page.PageSize);
        Assert.Equal(printingId, found.PrintingId); Assert.Equal(setId, found.SetId);
        Assert.Equal(provider.Artwork, found.ArtworkUrl);
        var details = (await client.GetFromJsonAsync<CatalogPrintingDetails>($"/api/v1/catalog/printings/{found.PrintingId}"))!;
        Assert.Equal(cardId, details.CardId); Assert.Equal(setId, details.SetId);
        Assert.Equal(provider.Artwork, details.ArtworkUrl);
        Assert.Equal("007 / 100", details.CollectorNumber); Assert.Equal("en", details.Language); Assert.Equal("rare", details.Rarity);
        Assert.Equal(variantId, details.Variants.Single(x => x.Code == "normal").Id);
        await Authenticate(client);
        var add = await PostCollectionItem(client, new { printingId, variantId, quantity = 2, condition = "NEAR_MINT" });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var added = (await add.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!;
        Assert.Equal(2, added.CreatedItems.Count);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var collection = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
            Assert.Equal(1, await collection.Entries.CountAsync(x => x.Id == added.CollectionEntryId));
            Assert.Equal(2, await collection.Items.CountAsync(x => x.CollectionEntryId == added.CollectionEntryId));
            var catalog = scope.ServiceProvider.GetRequiredService<ICatalogCollectionReader>();
            Assert.Empty(await catalog.SearchPrintingIds(null, null, null, default));
            Assert.Equal(provider.Artwork, Assert.Single(await catalog.GetPrintings([printingId], default)).ArtworkUrl);
            Assert.Equal(variantId, Assert.Single(await catalog.GetVariants([variantId], default)).VariantId);
        }
        // Unfiltered Collection no longer materializes every catalog ID, but must still return this entry.
        var collectionPage = (await client.GetFromJsonAsync<CollectionPageDto>("/api/v1/me/collection"))!;
        Assert.Equal(added.CollectionEntryId, Assert.Single(collectionPage.Items).CollectionEntryId);
        provider.Artwork = "https://example.com/updated.png";
        provider.Rarity = "Ultra Rare";
        provider.Variants = [new("holo", "Holo", "holo")];
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await Sync(scope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default);
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var updated = await db.Printings.Include(x => x.Variants).SingleAsync(x => x.Id == printingId);
            Assert.Equal(cardId, updated.CardId); Assert.Equal("Ultra Rare", updated.RawRarity); Assert.Equal("ultra-rare", updated.Rarity);
            Assert.False(updated.Variants.Single(x => x.Id == variantId).IsActive);
            Assert.False(updated.Variants.Single(x => x.Id == reverseId).IsActive);
            Assert.Equal(3, updated.Variants.Count);
            // Historical references remain resolvable even while no longer offered by public details.
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<ICatalogCollectionReader>().GetVariant(variantId, default));
        }
        details = (await client.GetFromJsonAsync<CatalogPrintingDetails>($"/api/v1/catalog/printings/{printingId}"))!;
        Assert.Equal("holo", Assert.Single(details.Variants).Code); Assert.Equal(provider.Artwork, details.ArtworkUrl);
        var historical = await client.GetAsync($"/api/v1/me/collection/entries/{added.CollectionEntryId}");
        historical.EnsureSuccessStatusCode();
        // Reappearance reactivates the exact original GUID.
        provider.Variants = [new("normal", "Normal", "normal")];
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            await Sync(scope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default);
        details = (await client.GetFromJsonAsync<CatalogPrintingDetails>($"/api/v1/catalog/printings/{printingId}"))!;
        Assert.Equal(variantId, Assert.Single(details.Variants).Id);
    }

    [Fact]
    public async Task VariantLifecycleProtectsNewCollectionAdditionsWhileKeepingHistoricalItemsReadable()
    {
        var provider = new FixtureProvider { Variants = [new("normal", "Normal", "normal"), new("reverse", "Reverse", "reverse")] };
        Guid printingId, normalVariantId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await Sync(scope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default);
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var printing = await db.Printings.Include(x => x.Variants).SingleAsync(x => x.Set.Name == provider.SetName);
            printingId = printing.Id;
            normalVariantId = printing.Variants.Single(x => x.Code == "normal").Id;
        }
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);

        // Historical add while the Variant is still active.
        var historicalAdd = await PostCollectionItem(client, new { printingId, variantId = normalVariantId, quantity = 1, condition = "NEAR_MINT" });
        Assert.Equal(HttpStatusCode.Created, historicalAdd.StatusCode);
        var historicalEntryId = (await historicalAdd.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!.CollectionEntryId;

        // Sync #2: provider stops offering the "normal" treatment -> that Variant becomes inactive.
        // The Printing itself stays active because "holo" is still offered.
        provider.Variants = [new("holo", "Holo", "holo")];
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await Sync(scope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default);
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.True((await db.Printings.SingleAsync(x => x.Id == printingId)).IsActive);
            Assert.False((await db.Variants.SingleAsync(x => x.Id == normalVariantId)).IsActive);
        }

        // Historical Collection access to the now-inactive Variant must keep working.
        var historicalRead = await client.GetAsync($"/api/v1/me/collection/entries/{historicalEntryId}");
        historicalRead.EnsureSuccessStatusCode();

        // New Collection additions referencing the inactive Variant are rejected even though the Printing is active.
        var rejected = await PostCollectionItem(client, new { printingId, variantId = normalVariantId, quantity = 1, condition = "NEAR_MINT" });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
    }

    [Fact]
    public async Task PrintingLifecycleFollowsProviderAvailabilityAcrossSyncsAndProtectsCollectionAndPublicReads()
    {
        var provider = new MultiPrintingProvider();
        Guid printingAId, printingBId, setId;
        // Sync #1: both printings present -> both active.
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await Sync(scope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default);
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var printings = await db.Printings.Where(x => x.Set.Name == provider.SetName).OrderBy(x => x.CollectorNumber).ToArrayAsync();
            Assert.Equal(2, printings.Length);
            Assert.All(printings, x => Assert.True(x.IsActive));
            printingAId = printings[0].Id; printingBId = printings[1].Id; setId = printings[0].SetId;
        }
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var addBeforeInactive = await PostCollectionItem(client, new { printingId = printingBId, quantity = 1, condition = "NEAR_MINT" });
        Assert.Equal(HttpStatusCode.Created, addBeforeInactive.StatusCode);
        var historicalAdd = (await addBeforeInactive.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!;

        // Sync #2: provider stops returning Printing B -> B becomes inactive, A stays active. A failed sync must not have caused this.
        provider.IncludeSecondPrinting = false;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await Sync(scope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default);
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.True((await db.Printings.SingleAsync(x => x.Id == printingAId)).IsActive);
            Assert.False((await db.Printings.SingleAsync(x => x.Id == printingBId)).IsActive);
        }

        // Search only returns the active Printing.
        var search = await client.GetFromJsonAsync<CatalogSearchPage>($"/api/v1/catalog/search?q={Uri.EscapeDataString(provider.CardName)}&game=pokemon&page=1&pageSize=10");
        Assert.DoesNotContain(search!.Items, x => x.PrintingId == printingBId);
        Assert.Contains(search.Items, x => x.PrintingId == printingAId);

        // Public detail hides the inactive Printing from anonymous callers.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/catalog/printings/{printingBId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/catalog/printings/{printingAId}")).StatusCode);

        // Historical Collection access to the now-inactive Printing must keep working.
        var historicalRead = await client.GetAsync($"/api/v1/me/collection/entries/{historicalAdd.CollectionEntryId}");
        historicalRead.EnsureSuccessStatusCode();

        // New Collection additions referencing the inactive Printing are rejected.
        var rejected = await PostCollectionItem(client, new { printingId = printingBId, quantity = 1, condition = "NEAR_MINT" });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        // A sync that fails outright must not deactivate any Printing of the set.
        provider.FailAfterFetch = true;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Sync(scope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default));
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.True((await db.Printings.SingleAsync(x => x.Id == printingAId)).IsActive);
            Assert.False((await db.Printings.SingleAsync(x => x.Id == printingBId)).IsActive);
        }
        provider.FailAfterFetch = false;

        // Sync #3: provider brings Printing B back -> same GUID reactivated.
        provider.IncludeSecondPrinting = true;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await Sync(scope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default);
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var printingB = await db.Printings.SingleAsync(x => x.Id == printingBId);
            Assert.True(printingB.IsActive);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/catalog/printings/{printingBId}")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentSyncsIncludingAllAndSetAreSerializedByPostgresLock()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FixtureProvider { BeforeDetails = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); } };
        await using var firstScope = fixture.Factory.Services.CreateAsyncScope();
        await using var secondScope = fixture.Factory.Services.CreateAsyncScope();
        var first = Sync(firstScope.ServiceProvider, provider).Synchronize("fake", "all", default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var secondId = await Sync(secondScope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("skipped", (await secondScope.ServiceProvider.GetRequiredService<CatalogDbContext>().SyncRuns.SingleAsync(x => x.Id == secondId)).Status);
        }
        finally { release.TrySetResult(); }
        var firstId = await first;
        Assert.Equal("completed", (await firstScope.ServiceProvider.GetRequiredService<CatalogDbContext>().SyncRuns.SingleAsync(x => x.Id == firstId)).Status);
    }

    [Theory]
    [InlineData("429", "transient:http_429")]
    [InlineData("500", "transient:http_500")]
    [InlineData("json", "permanent:invalid_json")]
    [InlineData("timeout", "transient:timeout")]
    public async Task RealAdapterFailuresAreRecordedInSyncRuns(string failure, string expectedCategory)
    {
        using var handler = new FailureHandler(failure);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.tcgdex.net/v2/") };
        var adapter = new TcgDexProvider(client, Options.Create(new TcgDexOptions { Timeout = 1, RetryCount = 0 }));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var sync = Sync(scope.ServiceProvider, adapter);
        var syncScope = Guid.NewGuid().ToString("N");
        var error = await Assert.ThrowsAsync<CatalogProviderException>(() => sync.Synchronize("tcgdex", syncScope, default));
        Assert.Equal(failure != "json", error.IsTransient);
        var run = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SyncRuns.SingleAsync(x => x.Scope == syncScope);
        Assert.Equal("failed", run.Status); Assert.Equal(expectedCategory, run.ErrorCategory); Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task CancellationIsRecordedAndReleasesLock()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new FixtureProvider { BeforeDetails = _ => { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; } };
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sync(scope.ServiceProvider, provider).Synchronize("fake", provider.SetId, cancellation.Token));
        var run = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SyncRuns.SingleAsync(x => x.Scope == provider.SetId);
        Assert.Equal("cancelled", run.Status); Assert.Equal("cancelled", run.ErrorCategory);
        provider.BeforeDetails = null;
        await using var nextScope = fixture.Factory.Services.CreateAsyncScope();
        var next = await Sync(nextScope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default);
        Assert.Equal("completed", (await nextScope.ServiceProvider.GetRequiredService<CatalogDbContext>().SyncRuns.SingleAsync(x => x.Id == next)).Status);
    }

    [Fact]
    public async Task FailedSetRollsBackAndDoesNotFlushPendingEntitiesWhenRecordingFailure()
    {
        var provider = new FixtureProvider { DuplicatePrinting = true };
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<DbUpdateException>(() => Sync(scope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default));
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.False(await db.Sets.AnyAsync(x => x.Name == provider.SetName));
        Assert.False(await db.Cards.AnyAsync(x => x.Name == provider.CardName));
        var run = await db.SyncRuns.SingleAsync(x => x.Scope == provider.SetId);
        Assert.Equal("failed", run.Status);
        Assert.Equal(0, run.RecordsCreated);
        Assert.Equal(0, run.RecordsUpdated);
        Assert.Equal(0, run.RecordsUnresolved);
        Assert.NotNull(run.CompletedAt);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    public async Task SearchRejectsInvalidPagination(string pagination)
    {
        using var client = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/catalog/search?q=pikachu&{pagination}")).StatusCode);
    }

    private static CatalogSyncService Sync(IServiceProvider services, ICatalogProvider provider) => new(services.GetRequiredService<CatalogDbContext>(), [provider], services.GetRequiredService<IClock>(), NullLogger<CatalogSyncService>.Instance);
    private static async Task Authenticate(HttpClient client)
    {
        var request = new RegisterRequest($"{Guid.NewGuid():N}@example.com", "Secure-Test-Password1!", "u" + Guid.NewGuid().ToString("N")[..20], "Collector");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/auth/register", request)).StatusCode);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken);
    }

    // AddCollectibleItems requires an Idempotency-Key; each call below represents a distinct
    // voluntary add, so a fresh key per call is correct (retries of the same call would reuse it).
    private static async Task<HttpResponseMessage> PostCollectionItem(HttpClient client, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/collection/items") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    private sealed class FixtureProvider : ICatalogProvider
    {
        private readonly string _key = Guid.NewGuid().ToString("N");
        public string Code => "fake";
        public string SetId => "set-" + _key;
        public string SetName => "Set " + _key;
        public string CardName => "Pikachu " + _key;
        public string Artwork { get; set; } = "https://example.com/pikachu.png";
        public string Rarity { get; set; } = "Rare";
        public IReadOnlyList<ProviderVariant> Variants { get; set; } = [new("normal", "Normal", "normal"), new("reverse", "Reverse", "reverse")];
        public bool DuplicatePrinting { get; set; }
        public Func<CancellationToken, Task>? BeforeDetails { get; set; }
        private ProviderSet Set => new(SetId, SetName, null, new DateOnly(2026, 1, 1));
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderSet>>([Set]);
        public async Task<ProviderSetDetails> GetSetDetails(string setId, CancellationToken cancellationToken)
        {
            if (BeforeDetails is not null) await BeforeDetails(cancellationToken);
            var printing = new ProviderPrinting("card-" + _key, CardName, "007 / 100", "en", Rarity, Artwork, Variants);
            // Different external identities claim the same canonical set/number/language. The DB must reject the entire batch.
            return new(Set, DuplicatePrinting ? [printing, printing with { ExternalId = "other-" + _key }] : [printing]);
        }
    }

    private sealed class MultiPrintingProvider : ICatalogProvider
    {
        private readonly string _key = Guid.NewGuid().ToString("N");
        public string Code => "fake";
        public string SetId => "set-" + _key;
        public string SetName => "Set " + _key;
        public string CardName => "Pikachu " + _key;
        public bool IncludeSecondPrinting { get; set; } = true;
        public bool FailAfterFetch { get; set; }
        private ProviderSet Set => new(SetId, SetName, null, new DateOnly(2026, 1, 1));
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderSet>>([Set]);
        public Task<ProviderSetDetails> GetSetDetails(string setId, CancellationToken cancellationToken)
        {
            if (FailAfterFetch) throw new InvalidOperationException("Simulated failure after fetching provider data.");
            var printingA = new ProviderPrinting("card-a-" + _key, CardName, "001 / 100", "en", "Rare", "https://example.com/a.png", [new("normal", "Normal", "normal")]);
            var printings = new List<ProviderPrinting> { printingA };
            if (IncludeSecondPrinting)
                printings.Add(new ProviderPrinting("card-b-" + _key, CardName, "002 / 100", "en", "Rare", "https://example.com/b.png", [new("normal", "Normal", "normal")]));
            return Task.FromResult(new ProviderSetDetails(Set, printings));
        }
    }

    private sealed class FailureHandler(string failure) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (failure == "timeout") await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return failure == "json" ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{bad", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage((HttpStatusCode)int.Parse(failure, System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
