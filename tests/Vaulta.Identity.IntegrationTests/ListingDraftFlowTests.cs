using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vaulta.Assets.Domain;
using Vaulta.Assets.Infrastructure;
using Vaulta.Catalog.Domain;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Collection.Domain;
using Vaulta.Collection.Infrastructure;
using Vaulta.Identity.Contracts;
using Vaulta.Marketplace.Application;
using Vaulta.Marketplace.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.Marketplace.Infrastructure;
using Vaulta.Orders.Contracts;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class ListingDraftFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task DraftWithoutPriceOrPhotosIsPrivateAndDoesNotReserveTheUnit()
    {
        using var api = fixture.Factory.CreateClient();
        var seller = await Register(api);
        var (itemId, printingId) = await Unit(seller.User.Id, "UNKNOWN");
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        var response = await api.PostAsJsonAsync("/api/v1/me/seller/listing-drafts", new
        {
            ClientDraftKey = "scanner-" + Guid.NewGuid().ToString("N"), CollectibleItemId = itemId,
            PrintingId = printingId, Condition = "UNKNOWN", PriceBrl = (decimal?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("draft", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("priceBrl").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/v1/marketplace/listings/{id}")).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<CollectionDbContext>().Items.SingleAsync(x => x.Id == itemId)).ListedById);
    }

    [Fact]
    public async Task CreateRetryKeepsIdentityAfterEditButDifferentOriginalPayloadConflicts()
    {
        using var api = fixture.Factory.CreateClient(); var seller = await Register(api);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        var (item, printing) = await Unit(seller.User.Id, "UNKNOWN");
        var request = new CreateListingDraftRequest("scan-" + Guid.NewGuid().ToString("N"), item, printing, null, "UNKNOWN", null, null);
        var draft = await Create(api, request);
        var edited = await Read(await api.PutAsJsonAsync(Route(draft.Id), new UpdateListingDraftRequest("NEAR_MINT", 85, "Real card", draft.Version)));
        Assert.Equal(85, edited.PriceBrl);
        var retry = await Create(api, request);
        Assert.Equal(draft.Id, retry.Id); Assert.Equal(edited.Version, retry.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsJsonAsync(Base, request with { PriceBrl = 90 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PutAsJsonAsync(Route(draft.Id), new UpdateListingDraftRequest("NEAR_MINT", 95, null, draft.Version))).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>().Listings.Where(x => x.CollectibleItemId == item).ToArrayAsync());
    }

    [Fact]
    public async Task ConcurrentCreateRetriesUseDurableUniqueness()
    {
        using var seed = fixture.Factory.CreateClient(); var seller = await Register(seed); var (item, printing) = await Unit(seller.User.Id, "UNKNOWN");
        using var a = fixture.Factory.CreateClient(); using var b = fixture.Factory.CreateClient();
        a.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken); b.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        var request = new CreateListingDraftRequest("scan-" + Guid.NewGuid().ToString("N"), item, printing, null, "UNKNOWN", null, null);
        var results = await Task.WhenAll(a.PostAsJsonAsync(Base, request), b.PostAsJsonAsync(Base, request));
        var first = await Read(results[0]); var second = await Read(results[1]);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task OtherAccountCannotReadEditAttachPublishOrCancelPrivateDraft()
    {
        using var api = fixture.Factory.CreateClient(); var seller = await Register(api); var other = await Register(api);
        var (item, printing) = await Unit(seller.User.Id, "NEAR_MINT");
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        var draft = await Create(api, new("scan-" + Guid.NewGuid().ToString("N"), item, printing, null, "NEAR_MINT", 90, null));
        api.DefaultRequestHeaders.Authorization = new("Bearer", other.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync(Route(draft.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.PutAsJsonAsync(Route(draft.Id), new UpdateListingDraftRequest("NEAR_MINT", 99, null, draft.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsJsonAsync(Route(draft.Id) + "/photos", new ListingDraftPhotoRequest(Guid.NewGuid(), "FRONT", 0, draft.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsJsonAsync(Route(draft.Id) + "/publish", new PublishListingDraftRequest("publish-" + draft.Id, draft.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync(Route(draft.Id) + "?version=" + draft.Version)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsJsonAsync(Base, new CreateListingDraftRequest("alien", item, printing, null, "NEAR_MINT", 90, null))).StatusCode);
    }

    [Fact]
    public async Task PublishRequiresPriceDeclaredConditionAndRealOwnedFrontAndBack()
    {
        using var api = fixture.Factory.CreateClient(); var seller = await Register(api);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken); await EnableSeller(api);
        var (item, printing) = await Unit(seller.User.Id, "UNKNOWN");
        var draft = await Create(api, new("scan-" + Guid.NewGuid().ToString("N"), item, printing, null, "UNKNOWN", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, (await Publish(api, draft)).StatusCode);
        draft = await Read(await api.PutAsJsonAsync(Route(draft.Id), new UpdateListingDraftRequest("UNKNOWN", 90, null, draft.Version)));
        Assert.Equal(HttpStatusCode.BadRequest, (await Publish(api, draft)).StatusCode);
        draft = await Read(await api.PutAsJsonAsync(Route(draft.Id), new UpdateListingDraftRequest("NEAR_MINT", 90, null, draft.Version)));
        Assert.Equal(HttpStatusCode.BadRequest, (await Publish(api, draft)).StatusCode);
        foreach (var assetId in new[] { await Asset(seller.User.Id, status: "pending"), await Asset(seller.User.Id, visibility: "public"), await Asset(seller.User.Id, purpose: "profile-avatar"), await Asset(Guid.NewGuid()) })
        {
            var invalid = await api.PostAsJsonAsync(Route(draft.Id) + "/photos", new ListingDraftPhotoRequest(assetId, "FRONT", 0, draft.Version));
            Assert.True(invalid.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
        }
        draft = await Photo(api, draft, await Asset(seller.User.Id), "FRONT");
        Assert.Equal(HttpStatusCode.BadRequest, (await Publish(api, draft)).StatusCode);
        draft = await Photo(api, draft, await Asset(seller.User.Id), "BACK");
        // The unit has UNKNOWN; declaring a listing condition alone cannot change its ownership record.
        Assert.Equal(HttpStatusCode.Conflict, (await Publish(api, draft)).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<CollectionDbContext>().Items.SingleAsync(x => x.Id == item)).ListedById);
    }

    [Fact]
    public async Task PublishResponseLossReturnsSameActiveListingAndWrongKeyConflicts()
    {
        using var api = fixture.Factory.CreateClient(); var seller = await Register(api);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken); await EnableSeller(api);
        var draft = await ReadyDraft(api, seller.User.Id);
        var request = new PublishListingDraftRequest("publish-" + draft.Id, draft.Version);
        var active = await Read(await api.PostAsJsonAsync(Route(draft.Id) + "/publish", request));
        var replay = await Read(await api.PostAsJsonAsync(Route(draft.Id) + "/publish", request));
        Assert.Equal(draft.Id, active.Id); Assert.Equal("active", replay.Status); Assert.Equal(active.Version, replay.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsJsonAsync(Route(draft.Id) + "/publish", request with { IdempotencyKey = "other" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsJsonAsync(Route(draft.Id) + "/publish", request with { Version = active.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync($"/api/v1/marketplace/listings/{active.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PutAsJsonAsync(Route(draft.Id), new UpdateListingDraftRequest("NEAR_MINT", 100, null, active.Version))).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.Equal(active.Id, (await scope.ServiceProvider.GetRequiredService<CollectionDbContext>().Items.SingleAsync(x => x.Id == active.CollectibleItemId)).ListedById);
    }

    [Fact]
    public async Task TwoDraftsForOneUnitCannotBothPublishAndDraftCannotBePurchased()
    {
        using var api = fixture.Factory.CreateClient(); var seller = await Register(api); var buyer = await Register(api);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken); await EnableSeller(api);
        var first = await ReadyDraft(api, seller.User.Id);
        var second = await Create(api, new("second-" + Guid.NewGuid().ToString("N"), first.CollectibleItemId, first.PrintingId, null, "NEAR_MINT", 95, null));
        second = await Photo(api, second, await Asset(seller.User.Id), "FRONT"); second = await Photo(api, second, await Asset(seller.User.Id), "BACK");
        api.DefaultRequestHeaders.Authorization = new("Bearer", buyer.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(first.Id, null, null, null, null, null, null, null, null))).StatusCode);
        using var a = fixture.Factory.CreateClient(); using var b = fixture.Factory.CreateClient();
        a.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken); b.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        var results = await Task.WhenAll(Publish(a, first), Publish(b, second));
        Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var listings = await scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>().Listings.Where(x => x.CollectibleItemId == first.CollectibleItemId && x.Status == "active").ToArrayAsync();
        Assert.Single(listings);
        Assert.Equal(listings[0].Id, (await scope.ServiceProvider.GetRequiredService<CollectionDbContext>().Items.SingleAsync(x => x.Id == first.CollectibleItemId)).ListedById);
    }

    [Fact]
    public async Task PhotoRemovalAndCancellationUseVersionAndCancelledDraftRetryStaysCancelled()
    {
        using var api = fixture.Factory.CreateClient(); var seller = await Register(api);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        var (item, printing) = await Unit(seller.User.Id, "UNKNOWN");
        var request = new CreateListingDraftRequest("scan-" + Guid.NewGuid().ToString("N"), item, printing, null, "UNKNOWN", null, null);
        var draft = await Create(api, request); var stale = draft.Version;
        var asset = await Asset(seller.User.Id); draft = await Photo(api, draft, asset, "FRONT");
        Assert.Equal(HttpStatusCode.Conflict, (await api.DeleteAsync(Route(draft.Id) + $"/photos/{asset}?version={stale}")).StatusCode);
        draft = await Read(await api.DeleteAsync(Route(draft.Id) + $"/photos/{asset}?version={draft.Version}"));
        Assert.Empty(draft.Photos);
        draft = await Read(await api.DeleteAsync(Route(draft.Id) + "?version=" + draft.Version));
        Assert.Equal("cancelled", draft.Status);
        Assert.Equal("cancelled", (await Create(api, request)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/v1/marketplace/listings/{draft.Id}")).StatusCode);
    }

    [Fact]
    public async Task InterruptedPublicationAfterReservationRecoversUsingSameKey()
    {
        using var api = fixture.Factory.CreateClient(); var seller = await Register(api);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken); await EnableSeller(api);
        var draft = await ReadyDraft(api, seller.User.Id);
        await using var interruptedFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMarketplaceCollection>();
            services.AddScoped<IMarketplaceCollection>(sp => new InterruptedCollection(new MarketplaceCollectionAdapter(sp.GetRequiredService<Vaulta.Collection.Application.ICollectionQueries>(), sp.GetRequiredService<Vaulta.Collection.Application.ICollectionMarketplace>())));
        }));
        using var interrupted = interruptedFactory.CreateClient(); interrupted.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Publish(interrupted, draft)).StatusCode);
        var pending = (await api.GetFromJsonAsync<ListingDraftDto>(Route(draft.Id)))!;
        Assert.Equal("publishing", pending.Status);
        var recovered = await Read(await Publish(api, draft));
        Assert.Equal(draft.Id, recovered.Id); Assert.Equal("active", recovered.Status);
    }

    [Fact]
    public async Task RecoveryCannotPublishDeletedPhotoAndReleasesItsMatchingReservation()
    {
        using var api = fixture.Factory.CreateClient(); var seller = await Register(api);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken); await EnableSeller(api);
        var draft = await ReadyDraft(api, seller.User.Id);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var market = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var pending = await market.Listings.Include(x => x.Photos).SingleAsync(x => x.Id == draft.Id);
            pending.PrepareDraftPublication("publish-" + draft.Id, draft.Version, DateTimeOffset.UtcNow); await market.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IMarketplaceCollection>().ReserveListingItem(pending, default);
            var assets = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            (await assets.Assets.SingleAsync(x => x.Id == draft.Photos[0].AssetId)).Status = "deleted";
            await assets.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Publish(api, draft)).StatusCode);
        var cancelled = (await api.GetFromJsonAsync<ListingDraftDto>(Route(draft.Id)))!; Assert.Equal("cancelled", cancelled.Status);
        await using var inspect = fixture.Factory.Services.CreateAsyncScope();
        Assert.Null((await inspect.ServiceProvider.GetRequiredService<CollectionDbContext>().Items.SingleAsync(x => x.Id == draft.CollectibleItemId)).ListedById);
    }

    [Fact]
    public async Task ConcurrentRecoveryOfSamePublicationCannotCancelTheSuccessfulReservation()
    {
        using var api = fixture.Factory.CreateClient(); var seller = await Register(api);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken); await EnableSeller(api);
        var draft = await ReadyDraft(api, seller.User.Id);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var market = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var pending = await market.Listings.Include(x => x.Photos).SingleAsync(x => x.Id == draft.Id);
            pending.PrepareDraftPublication("publish-" + draft.Id, draft.Version, DateTimeOffset.UtcNow); await market.SaveChangesAsync();
        }
        var barrier = new RacingRecovery();
        await using var racingFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMarketplaceCollection>();
            services.AddScoped<IMarketplaceCollection>(sp => new RacingCollection(
                new MarketplaceCollectionAdapter(sp.GetRequiredService<Vaulta.Collection.Application.ICollectionQueries>(), sp.GetRequiredService<Vaulta.Collection.Application.ICollectionMarketplace>()),
                sp.GetRequiredService<Vaulta.Collection.Application.ICollectionStore>(), barrier));
        }));
        using var a = racingFactory.CreateClient(); using var b = racingFactory.CreateClient();
        a.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken); b.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        var results = await Task.WhenAll(Publish(a, draft), Publish(b, draft));
        Assert.Contains(results, x => x.IsSuccessStatusCode);
        var recovered = (await api.GetFromJsonAsync<ListingDraftDto>(Route(draft.Id)))!;
        Assert.Equal("active", recovered.Status);
        await using var inspect = fixture.Factory.Services.CreateAsyncScope();
        Assert.Equal(draft.Id, (await inspect.ServiceProvider.GetRequiredService<CollectionDbContext>().Items.SingleAsync(x => x.Id == draft.CollectibleItemId)).ListedById);
    }

    private sealed class RacingRecovery
    {
        private int _readers;
        public int Readers => Volatile.Read(ref _readers);
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FailedRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Read(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _readers) == 2) _both.TrySetResult();
            try { await _both.Task.WaitAsync(TimeSpan.FromSeconds(2), ct); }
            catch (TimeoutException) when (Readers == 1) { }
        }
    }

    private sealed class RacingCollection(IMarketplaceCollection inner, Vaulta.Collection.Application.ICollectionStore collection, RacingRecovery barrier) : IMarketplaceCollection
    {
        public Task<MarketplaceItemIdentity?> GetItemIdentity(Guid user, Guid entry, CancellationToken ct) => inner.GetItemIdentity(user, entry, ct);
        public Task<Vaulta.Collection.Contracts.CollectibleItemDto?> GetCollectibleItem(Guid user, Guid item, CancellationToken ct) => inner.GetCollectibleItem(user, item, ct);
        public async Task ReserveListingItem(Listing listing, CancellationToken ct)
        {
            // Both scopes retain the same original collection version before either reservation can be committed.
            await collection.FindItem(listing.SellerUserId, listing.CollectibleItemId, ct);
            await barrier.Read(ct);
            await inner.ReserveListingItem(listing, ct);
            if (barrier.Readers == 2) await barrier.FailedRelease.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
        public async Task ReleaseListingItem(Listing listing, CancellationToken ct)
        {
            try { await inner.ReleaseListingItem(listing, ct); }
            finally { barrier.FailedRelease.TrySetResult(); }
        }
    }

    private sealed class InterruptedCollection(IMarketplaceCollection inner) : IMarketplaceCollection
    {
        public Task<MarketplaceItemIdentity?> GetItemIdentity(Guid user, Guid entry, CancellationToken ct) => inner.GetItemIdentity(user, entry, ct);
        public Task<Vaulta.Collection.Contracts.CollectibleItemDto?> GetCollectibleItem(Guid user, Guid item, CancellationToken ct) => inner.GetCollectibleItem(user, item, ct);
        public async Task ReserveListingItem(Listing listing, CancellationToken ct) { await inner.ReserveListingItem(listing, ct); throw new IOException("Interrupted after durable reservation."); }
        public Task ReleaseListingItem(Listing listing, CancellationToken ct) => inner.ReleaseListingItem(listing, ct);
    }

    private const string Base = "/api/v1/me/seller/listing-drafts";
    private static string Route(Guid id) => Base + "/" + id;
    private static async Task<ListingDraftDto> Read(HttpResponseMessage response) { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<ListingDraftDto>())!; }
    private static Task<ListingDraftDto> Create(HttpClient api, CreateListingDraftRequest request) => CreateCore(api, request);
    private static async Task<ListingDraftDto> CreateCore(HttpClient api, CreateListingDraftRequest request) => await Read(await api.PostAsJsonAsync(Base, request));
    private static Task<HttpResponseMessage> Publish(HttpClient api, ListingDraftDto draft) => api.PostAsJsonAsync(Route(draft.Id) + "/publish", new PublishListingDraftRequest("publish-" + draft.Id, draft.Version));
    private static async Task EnableSeller(HttpClient api) => (await api.PostAsJsonAsync("/api/v1/me/seller", new SellerProfileRequest(null, null, null, null, null))).EnsureSuccessStatusCode();
    private static async Task<ListingDraftDto> Photo(HttpClient api, ListingDraftDto draft, Guid asset, string type) => await Read(await api.PostAsJsonAsync(Route(draft.Id) + "/photos", new ListingDraftPhotoRequest(asset, type, type == "FRONT" ? 0 : 1, draft.Version)));
    private async Task<ListingDraftDto> ReadyDraft(HttpClient api, Guid user)
    {
        var (item, printing) = await Unit(user, "NEAR_MINT");
        var draft = await Create(api, new("scan-" + Guid.NewGuid().ToString("N"), item, printing, null, "NEAR_MINT", 90, null));
        draft = await Photo(api, draft, await Asset(user), "FRONT"); return await Photo(api, draft, await Asset(user), "BACK");
    }
    private async Task<Guid> Asset(Guid owner, string status = "ready", string visibility = "private", string purpose = "collection-item")
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        var asset = new Asset { Id = Guid.NewGuid(), ObjectKey = "test/" + Guid.NewGuid(), Purpose = purpose, Visibility = visibility, ContentType = "image/jpeg", ContentLength = 5, OwnerId = owner, Status = status, CreatedAt = DateTimeOffset.UtcNow, ConfirmedAt = status == "ready" ? DateTimeOffset.UtcNow : null };
        db.Assets.Add(asset); await db.SaveChangesAsync(); return asset.Id;
    }

    private async Task<(Guid ItemId, Guid PrintingId)> Unit(Guid userId, string condition)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = DateTimeOffset.UtcNow;
        var game = new Game { Id = Guid.NewGuid(), Code = "draft-" + Guid.NewGuid().ToString("N")[..20], Name = "Draft game" };
        var card = new Card { Id = Guid.NewGuid(), GameId = game.Id, Name = "Draft card", NormalizedName = "DRAFT CARD" };
        var set = new Set { Id = Guid.NewGuid(), GameId = game.Id, Name = "Draft set", NormalizedName = "DRAFT SET" };
        var printing = new Printing { Id = Guid.NewGuid(), CardId = card.Id, SetId = set.Id, CollectorNumber = "1", NormalizedCollectorNumber = "1", Language = "en" };
        catalog.Games.Add(game); catalog.Cards.Add(card); catalog.Sets.Add(set); catalog.Printings.Add(printing); await catalog.SaveChangesAsync();
        var entry = CollectionEntry.Create(userId, printing.Id, null, now);
        var item = entry.AddItems(1, condition, null, null, null, now).Single();
        var collection = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
        collection.Entries.Add(entry); collection.Items.Add(item); await collection.SaveChangesAsync();
        return (item.Id, printing.Id);
    }

    private static async Task<AuthResponse> Register(HttpClient api)
    {
        var request = new RegisterRequest($"{Guid.NewGuid():N}@example.test", "Secure-Test-Password1!", "u" + Guid.NewGuid().ToString("N")[..20], "User");
        (await api.PostAsJsonAsync("/api/v1/auth/register", request)).EnsureSuccessStatusCode();
        var login = await api.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password));
        login.EnsureSuccessStatusCode(); return (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
    }
}
