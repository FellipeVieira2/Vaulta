using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Collection.Contracts;
using Vaulta.Identity.Contracts;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

/// <summary>
/// Dedicated HTTP-level hardening suite for the Collection bounded context: auth, quantity/entry
/// identity, ownership, optimistic concurrency, soft delete, summary/filters/pagination, global
/// sort=name ordering, advisory-lock concurrency and Idempotency-Key retry semantics for
/// POST /api/v1/me/collection/items. Domain-level rules (condition canonicalization, aggregate
/// invariants) are covered by Vaulta.Identity.UnitTests.CollectionDomainTests instead.
/// </summary>
[Collection("api")]
public sealed class CollectionApiTests(ApiFixture fixture)
{
    [Fact]
    public async Task CollectionEndpointsRequireAuthentication()
    {
        using var client = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/collection")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/me/collection/items/{Guid.NewGuid()}")).StatusCode);
        var post = await client.PostAsJsonAsync("/api/v1/me/collection/items", new { printingId = Guid.NewGuid(), quantity = 1, condition = "NEAR_MINT" });
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [Fact]
    public async Task AddingItemsWithoutIdempotencyKeyIsRejected()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, _) = await CreateCatalogPrinting("Squirtle");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/collection/items")
        {
            Content = JsonContent.Create(new { printingId, quantity = 1, condition = "NEAR_MINT" })
        };
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddingQuantityCreatesOneEntryAndDistinctPhysicalItems()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, variantIds) = await CreateCatalogPrinting("Charmander");

        var response = await PostItems(client, new { printingId, variantId = variantIds[0], quantity = 5, condition = "NEAR_MINT" }, "add-quantity-1");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var added = (await response.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!;

        Assert.Equal(5, added.Quantity);
        Assert.Equal(5, added.CreatedItems.Count);
        Assert.Equal(5, added.CreatedItems.Select(x => x.Id).Distinct().Count());
        Assert.Equal(5, added.CreatedItems.Select(x => x.Version).Distinct().Count());

        var entry = await client.GetFromJsonAsync<CollectionEntryDetailsDto>($"/api/v1/me/collection/entries/{added.CollectionEntryId}");
        Assert.Equal(5, entry!.Quantity);
    }

    [Fact]
    public async Task AddingAgainToSameIdentityReusesTheSameEntryAndAccumulatesQuantity()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, variantIds) = await CreateCatalogPrinting("Bulbasaur");

        var first = await PostItems(client, new { printingId, variantId = variantIds[0], quantity = 3, condition = "NEAR_MINT" }, "reuse-1");
        var second = await PostItems(client, new { printingId, variantId = variantIds[0], quantity = 2, condition = "LIGHTLY_PLAYED" }, "reuse-2");
        var firstBody = (await first.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!;
        var secondBody = (await second.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!;

        Assert.Equal(firstBody.CollectionEntryId, secondBody.CollectionEntryId);
        var entry = await client.GetFromJsonAsync<CollectionEntryDetailsDto>($"/api/v1/me/collection/entries/{firstBody.CollectionEntryId}");
        Assert.Equal(5, entry!.Quantity);
    }

    [Fact]
    public async Task DifferentVariantsOfTheSamePrintingCreateDifferentEntries()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, variantIds) = await CreateCatalogPrinting("Ivysaur", [new("normal", "Normal", "normal"), new("reverse", "Reverse", "reverse")]);

        var a = await PostItems(client, new { printingId, variantId = variantIds[0], quantity = 1, condition = "NEAR_MINT" }, "variant-a");
        var b = await PostItems(client, new { printingId, variantId = variantIds[1], quantity = 1, condition = "NEAR_MINT" }, "variant-b");
        var entryA = (await a.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!.CollectionEntryId;
        var entryB = (await b.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!.CollectionEntryId;

        Assert.NotEqual(entryA, entryB);
    }

    [Fact]
    public async Task AddingWithoutVariantHasItsOwnEntryAndIsReusedOnRetry()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, _) = await CreateCatalogPrinting("Venusaur");

        var first = await PostItems(client, new { printingId, quantity = 1, condition = "NEAR_MINT" }, "no-variant-1");
        var second = await PostItems(client, new { printingId, quantity = 1, condition = "NEAR_MINT" }, "no-variant-2");
        var entryA = (await first.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!.CollectionEntryId;
        var entryB = (await second.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!.CollectionEntryId;

        Assert.Equal(entryA, entryB);
    }

    [Fact]
    public async Task DifferentUsersAddingTheSamePrintingAndVariantGetSeparateEntries()
    {
        using var clientA = fixture.Factory.CreateClient();
        using var clientB = fixture.Factory.CreateClient();
        await Authenticate(clientA);
        await Authenticate(clientB);
        var (printingId, variantIds) = await CreateCatalogPrinting("Caterpie");

        var a = await PostItems(clientA, new { printingId, variantId = variantIds[0], quantity = 1, condition = "NEAR_MINT" }, "user-a");
        var b = await PostItems(clientB, new { printingId, variantId = variantIds[0], quantity = 1, condition = "NEAR_MINT" }, "user-b");
        var entryA = (await a.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!.CollectionEntryId;
        var entryB = (await b.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!.CollectionEntryId;

        Assert.NotEqual(entryA, entryB);
    }

    [Fact]
    public async Task AddingToUnknownPrintingReturnsNotFound()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var response = await PostItems(client, new { printingId = Guid.NewGuid(), quantity = 1, condition = "NEAR_MINT" }, "unknown-printing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddingWithUnknownVariantReturnsNotFound()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, _) = await CreateCatalogPrinting("Weedle");
        var response = await PostItems(client, new { printingId, variantId = Guid.NewGuid(), quantity = 1, condition = "NEAR_MINT" }, "unknown-variant");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddingWithVariantFromAnotherPrintingIsRejected()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingA, _) = await CreateCatalogPrinting("Pidgey");
        var (_, variantsB) = await CreateCatalogPrinting("Rattata");

        var response = await PostItems(client, new { printingId = printingA, variantId = variantsB[0], quantity = 1, condition = "NEAR_MINT" }, "cross-printing-variant");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatingAnItemChangesFieldsAndAdvancesVersion()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, variantIds) = await CreateCatalogPrinting("Spearow");
        var added = (await (await PostItems(client, new { printingId, variantId = variantIds[0], quantity = 1, condition = "NEAR_MINT" }, "update-flow")).Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!;
        var itemId = added.CreatedItems.Single().Id;
        var originalVersion = added.CreatedItems.Single().Version;

        var update = await client.PutAsJsonAsync($"/api/v1/me/collection/items/{itemId}", new
        {
            condition = "LIGHTLY_PLAYED",
            acquisitionPrice = new { amount = 12.5m, currency = "BRL" },
            acquisitionDate = new DateOnly(2025, 12, 1),
            notes = "updated notes",
            version = originalVersion
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var item = await client.GetFromJsonAsync<CollectibleItemDto>($"/api/v1/me/collection/items/{itemId}");
        Assert.Equal("LIGHTLY_PLAYED", item!.Condition);
        Assert.Equal("updated notes", item.Notes);
        Assert.Equal(12.5m, item.AcquisitionPrice!.Amount);
        Assert.NotEqual(originalVersion, item.Version);
    }

    [Fact]
    public async Task UpdatingWithStaleVersionReturnsConflict()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, variantIds) = await CreateCatalogPrinting("Fearow");
        var added = (await (await PostItems(client, new { printingId, variantId = variantIds[0], quantity = 1, condition = "NEAR_MINT" }, "concurrency-flow")).Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!;
        var itemId = added.CreatedItems.Single().Id;
        var staleVersion = added.CreatedItems.Single().Version;

        var first = await client.PutAsJsonAsync($"/api/v1/me/collection/items/{itemId}", new { condition = "LIGHTLY_PLAYED", acquisitionPrice = (object?)null, acquisitionDate = (DateOnly?)null, notes = (string?)null, version = staleVersion });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PutAsJsonAsync($"/api/v1/me/collection/items/{itemId}", new { condition = "DAMAGED", acquisitionPrice = (object?)null, acquisitionDate = (DateOnly?)null, notes = (string?)null, version = staleVersion });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task DeletingAnItemIsSoftAndHidesItFromNormalReads()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, variantIds) = await CreateCatalogPrinting("Ekans");
        var added = (await (await PostItems(client, new { printingId, variantId = variantIds[0], quantity = 2, condition = "NEAR_MINT" }, "delete-flow")).Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!;
        var toRemove = added.CreatedItems[0];

        var delete = await client.DeleteAsync($"/api/v1/me/collection/items/{toRemove.Id}?version={toRemove.Version}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/me/collection/items/{toRemove.Id}")).StatusCode);

        var entry = await client.GetFromJsonAsync<CollectionEntryDetailsDto>($"/api/v1/me/collection/entries/{added.CollectionEntryId}");
        Assert.Equal(1, entry!.Quantity);
    }

    [Fact]
    public async Task SummaryAggregatesActiveItemsByGameAndCondition()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingA, variantsA) = await CreateCatalogPrinting("Sandshrew");
        var (printingB, variantsB) = await CreateCatalogPrinting("Nidoran");
        await PostItems(client, new { printingId = printingA, variantId = variantsA[0], quantity = 2, condition = "NEAR_MINT" }, "summary-a");
        await PostItems(client, new { printingId = printingB, variantId = variantsB[0], quantity = 3, condition = "DAMAGED" }, "summary-b");

        var summary = await client.GetFromJsonAsync<CollectionSummaryDto>("/api/v1/me/collection/summary");
        Assert.Equal(2, summary!.TotalEntries);
        Assert.Equal(5, summary.TotalItems);
        Assert.Equal(5, summary.ItemsByGame["pokemon"]);
        Assert.Equal(2, summary.ItemsByCondition["NEAR_MINT"]);
        Assert.Equal(3, summary.ItemsByCondition["DAMAGED"]);
    }

    [Fact]
    public async Task FiltersByQueryConditionAndVariantNarrowResults()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var cardSuffix = Guid.NewGuid().ToString("N")[..8];
        var (printingA, variantsA) = await CreateCatalogPrinting($"Oddish {cardSuffix}");
        var (printingB, variantsB) = await CreateCatalogPrinting($"Gloom {cardSuffix}");
        await PostItems(client, new { printingId = printingA, variantId = variantsA[0], quantity = 1, condition = "NEAR_MINT" }, "filter-a");
        await PostItems(client, new { printingId = printingB, variantId = variantsB[0], quantity = 1, condition = "DAMAGED" }, "filter-b");

        var byQuery = await client.GetFromJsonAsync<CollectionPageDto>($"/api/v1/me/collection?query={Uri.EscapeDataString("Oddish " + cardSuffix)}");
        Assert.Equal(printingA, Assert.Single(byQuery!.Items).PrintingId);

        var byCondition = await client.GetFromJsonAsync<CollectionPageDto>($"/api/v1/me/collection?query={Uri.EscapeDataString(cardSuffix)}&condition=DAMAGED");
        Assert.Equal(printingB, Assert.Single(byCondition!.Items).PrintingId);

        var byVariant = await client.GetFromJsonAsync<CollectionPageDto>($"/api/v1/me/collection?query={Uri.EscapeDataString(cardSuffix)}&variantId={variantsA[0]}");
        Assert.Equal(printingA, Assert.Single(byVariant!.Items).PrintingId);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task CollectionRejectsInvalidPagination(string pagination)
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/me/collection?{pagination}")).StatusCode);
    }

    [Fact]
    public async Task OwnershipPreventsOtherUsersFromReadingUpdatingOrDeletingAnItem()
    {
        using var owner = fixture.Factory.CreateClient();
        using var stranger = fixture.Factory.CreateClient();
        await Authenticate(owner);
        await Authenticate(stranger);
        var (printingId, variantIds) = await CreateCatalogPrinting("Vulpix");
        var added = (await (await PostItems(owner, new { printingId, variantId = variantIds[0], quantity = 1, condition = "NEAR_MINT" }, "ownership-flow")).Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!;
        var item = added.CreatedItems.Single();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/me/collection/items/{item.Id}")).StatusCode);
        var update = await stranger.PutAsJsonAsync($"/api/v1/me/collection/items/{item.Id}", new { condition = "DAMAGED", acquisitionPrice = (object?)null, acquisitionDate = (DateOnly?)null, notes = (string?)null, version = item.Version });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/v1/me/collection/items/{item.Id}?version={item.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/me/collection/entries/{added.CollectionEntryId}")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentAddsWithDifferentIdempotencyKeysAreSerializedByTheAdvisoryLockAndNeverLoseItems()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, variantIds) = await CreateCatalogPrinting("Growlithe");

        var first = PostItems(client, new { printingId, variantId = variantIds[0], quantity = 2, condition = "NEAR_MINT" }, "concurrent-a");
        var second = PostItems(client, new { printingId, variantId = variantIds[0], quantity = 3, condition = "NEAR_MINT" }, "concurrent-b");
        var responses = await Task.WhenAll(first, second);

        Assert.Equal(HttpStatusCode.Created, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);
        var entryId = (await responses[0].Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!.CollectionEntryId;
        var entry = await client.GetFromJsonAsync<CollectionEntryDetailsDto>($"/api/v1/me/collection/entries/{entryId}");
        Assert.Equal(5, entry!.Quantity);
    }

    [Fact]
    public async Task RetryingWithTheSameIdempotencyKeyAndSamePayloadDoesNotDuplicateItems()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, variantIds) = await CreateCatalogPrinting("Poliwag");
        var body = new { printingId, variantId = variantIds[0], quantity = 2, condition = "NEAR_MINT" };

        var first = await PostItems(client, body, "retry-key");
        var retry = await PostItems(client, body, "retry-key");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);

        var firstBody = (await first.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!;
        var retryBody = (await retry.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!;
        Assert.Equal(firstBody.CollectionEntryId, retryBody.CollectionEntryId);
        Assert.Equal(firstBody.CreatedItems.Select(x => x.Id), retryBody.CreatedItems.Select(x => x.Id));

        var entry = await client.GetFromJsonAsync<CollectionEntryDetailsDto>($"/api/v1/me/collection/entries/{firstBody.CollectionEntryId}");
        Assert.Equal(2, entry!.Quantity);
    }

    [Fact]
    public async Task ReusingTheSameIdempotencyKeyWithADifferentPayloadIsRejected()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, variantIds) = await CreateCatalogPrinting("Tentacool");

        var first = await PostItems(client, new { printingId, variantId = variantIds[0], quantity = 2, condition = "NEAR_MINT" }, "shared-key");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var conflicting = await PostItems(client, new { printingId, variantId = variantIds[0], quantity = 3, condition = "NEAR_MINT" }, "shared-key");
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);

        var entry = await client.GetFromJsonAsync<CollectionEntryDetailsDto>($"/api/v1/me/collection/entries/{(await first.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!.CollectionEntryId}");
        Assert.Equal(2, entry!.Quantity);
    }

    [Fact]
    public async Task DifferentIdempotencyKeysForTheSameRequestAreTreatedAsSeparateIntents()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var (printingId, variantIds) = await CreateCatalogPrinting("Slowpoke");
        var body = new { printingId, variantId = variantIds[0], quantity = 2, condition = "NEAR_MINT" };

        await PostItems(client, body, "intent-1");
        await PostItems(client, body, "intent-2");

        var page = await client.GetFromJsonAsync<CollectionPageDto>($"/api/v1/me/collection?query={Uri.EscapeDataString("Slowpoke")}");
        Assert.Equal(4, Assert.Single(page!.Items).Quantity);
    }

    [Fact]
    public async Task SortByNameOrdersTheWholeCollectionGloballyBeforePaginating()
    {
        using var client = fixture.Factory.CreateClient();
        await Authenticate(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var cards = new[] { "Abra", "Charizard", "Bulbasaur", "Dragonite", "Eevee" };
        foreach (var card in cards)
        {
            var (printingId, variantIds) = await CreateCatalogPrinting($"{card} {suffix}");
            await PostItems(client, new { printingId, variantId = variantIds[0], quantity = 1, condition = "NEAR_MINT" }, $"sort-{card}");
        }

        async Task<string[]> Page(int page) =>
            (await client.GetFromJsonAsync<CollectionPageDto>($"/api/v1/me/collection?query={Uri.EscapeDataString(suffix)}&sort=name&page={page}&pageSize=2"))!
                .Items.Select(x => x.CardName.Replace(" " + suffix, string.Empty, StringComparison.Ordinal)).ToArray();

        Assert.Equal(["Abra", "Bulbasaur"], await Page(1));
        Assert.Equal(["Charizard", "Dragonite"], await Page(2));
        Assert.Equal(["Eevee"], await Page(3));
    }

    private static Task<HttpResponseMessage> PostItems(HttpClient client, object body, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/collection/items") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private async Task<(Guid PrintingId, Guid[] VariantIds)> CreateCatalogPrinting(string cardName, IReadOnlyList<ProviderVariant>? variants = null)
    {
        var provider = new SimpleProvider(cardName, variants);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        await Sync(scope.ServiceProvider, provider).Synchronize("fake", provider.SetId, default);
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var printing = await db.Printings.Include(x => x.Variants).SingleAsync(x => x.Card.Name == cardName);
        return (printing.Id, printing.Variants.OrderBy(v => v.Code).Select(v => v.Id).ToArray());
    }

    private static CatalogSyncService Sync(IServiceProvider services, ICatalogProvider provider) =>
        new(services.GetRequiredService<CatalogDbContext>(), [provider], services.GetRequiredService<IClock>(), NullLogger<CatalogSyncService>.Instance);

    private static async Task Authenticate(HttpClient client)
    {
        var request = new RegisterRequest($"{Guid.NewGuid():N}@example.com", "Secure-Test-Password1!", "u" + Guid.NewGuid().ToString("N")[..20], "Collector");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/auth/register", request)).StatusCode);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken);
    }

    private sealed class SimpleProvider(string cardName, IReadOnlyList<ProviderVariant>? variants) : ICatalogProvider
    {
        private readonly string _key = Guid.NewGuid().ToString("N");
        public string Code => "fake";
        public string SetId => "set-" + _key;
        private ProviderSet Set => new(SetId, "Set " + _key, null, new DateOnly(2026, 1, 1));
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderSet>>([Set]);
        public Task<ProviderSetDetails> GetSetDetails(string setId, CancellationToken cancellationToken)
        {
            var printing = new ProviderPrinting("card-" + _key, cardName, "001/100", "en", "Rare", "https://example.com/" + _key + ".png",
                variants ?? [new ProviderVariant("normal", "Normal", "normal")]);
            return Task.FromResult(new ProviderSetDetails(Set, [printing]));
        }
    }
}
