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
        var add = await client.PostAsJsonAsync("/api/v1/me/collection/items", new { printingId, variantId, quantity = 2, condition = "NEAR_MINT" });
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
        public Func<CancellationToken, Task>? BeforeDetails { get; set; }
        private ProviderSet Set => new(SetId, SetName, null, new DateOnly(2026, 1, 1));
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderSet>>([Set]);
        public async Task<ProviderSetDetails> GetSetDetails(string setId, CancellationToken cancellationToken)
        {
            if (BeforeDetails is not null) await BeforeDetails(cancellationToken);
            return new(Set, [new("card-" + _key, CardName, "007 / 100", "en", Rarity, Artwork, Variants)]);
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
