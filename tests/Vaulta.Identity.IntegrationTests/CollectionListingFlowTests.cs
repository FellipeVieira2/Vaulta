using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Collection.Application;
using Vaulta.Collection.Contracts;
using Vaulta.Collection.Domain;
using Vaulta.Collection.Infrastructure;
using Vaulta.Identity.Contracts;
using Vaulta.Marketplace.Application;
using Vaulta.Marketplace.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.Marketplace.Infrastructure;
using Vaulta.Orders.Application;
using Vaulta.Orders.Contracts;
using Vaulta.Orders.Domain;
using Vaulta.Orders.Infrastructure;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class CollectionListingFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task PublicationValidatesOwnershipLocksChangesAndOnlyMatchingCancellationUnlocks()
    {
        using var api = fixture.Factory.CreateClient();
        var seller = await Register(api);
        var printing = await Printing();
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        (await api.PostAsJsonAsync("/api/v1/me/seller", new SellerProfileRequest(null, null, null, null, null))).EnsureSuccessStatusCode();
        api.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var added = await api.PostAsJsonAsync("/api/v1/me/collection/items", new AddCollectibleItemsRequest(printing, null, 1, "NM", new(30, "BRL"), null, "Private"));
        added.EnsureSuccessStatusCode();
        var itemId = (await added.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!.CreatedItems.Single().Id;
        var invalid = await api.PostAsJsonAsync("/api/v1/me/seller/listings", new CreateListingRequest(itemId, printing, null, "LP", 100, null));
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        var own = (await api.GetFromJsonAsync<CollectibleItemDto>($"/api/v1/me/collection/items/{itemId}"))!;
        Assert.Null(own.ListedById);
        var response = await api.PostAsJsonAsync("/api/v1/me/seller/listings", new CreateListingRequest(itemId, printing, null, "NM", 100, null));
        response.EnsureSuccessStatusCode(); var listing = (await response.Content.ReadFromJsonAsync<ListingDto>())!;
        own = (await api.GetFromJsonAsync<CollectibleItemDto>($"/api/v1/me/collection/items/{itemId}"))!;
        Assert.Equal(listing.Id, own.ListedById);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PutAsJsonAsync($"/api/v1/me/collection/items/{itemId}",
            new UpdateCollectibleItemRequest("LP", null, null, "Changed", own.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.DeleteAsync($"/api/v1/me/collection/items/{itemId}?version={own.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsJsonAsync("/api/v1/me/seller/listings", new CreateListingRequest(itemId, printing, null, "NM", 100, null))).StatusCode);
        (await api.DeleteAsync($"/api/v1/me/seller/listings/{listing.Id}?version={listing.Version}")).EnsureSuccessStatusCode();
        own = (await api.GetFromJsonAsync<CollectibleItemDto>($"/api/v1/me/collection/items/{itemId}"))!; Assert.Null(own.ListedById);
        response = await api.PostAsJsonAsync("/api/v1/me/seller/listings", new CreateListingRequest(itemId, printing, null, "NM", 100, null));
        response.EnsureSuccessStatusCode(); var replacement = (await response.Content.ReadFromJsonAsync<ListingDto>())!;
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICollectionMarketplace>().Release(seller.User.Id, itemId, listing.Id, default);
        var stored = await scope.ServiceProvider.GetRequiredService<CollectionDbContext>().Items.SingleAsync(x => x.Id == itemId);
        Assert.Equal(replacement.Id, stored.ListedById); Assert.Equal("Private", stored.Notes);
    }

    [Fact]
    public async Task BuyerReceiptTransfersOncePreservesPrivateHistoryAndReplayDoesNotRecreateRemovedItem()
    {
        using var api = fixture.Factory.CreateClient();
        var seller = await Register(api); var buyer = await Register(api);
        var printing = await Printing();
        var listing = await CollectionCommerceSeed.Listing(fixture.Factory.Services, seller.User.Id, printing, privatePhoto: true);
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(buyer.User.Id, seller.User.Id, listing.Id, listing.CollectibleItemId, printing, null, "NM", 100, 8,
            null, null, null, null, null, null, null, null, now);
        order.MarkAsPaid("pay_test", now); order.MarkAsShipped("tracking", now); listing.MarkAsSold(order.Id, now);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var market = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            market.SellerProfiles.Add(SellerProfile.Enable(seller.User.Id, null, null, null, null, null, now)); market.Listings.Add(listing); await market.SaveChangesAsync();
            var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>(); orders.Orders.Add(order); await orders.SaveChangesAsync();
        }
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.PostAsync($"/api/v1/orders/{order.Id}/deliver", null)).StatusCode);
        api.DefaultRequestHeaders.Authorization = new("Bearer", buyer.AccessToken);
        (await api.PostAsync($"/api/v1/orders/{order.Id}/deliver", null)).EnsureSuccessStatusCode();
        await using var a = fixture.Factory.Services.CreateAsyncScope(); await using var b = fixture.Factory.Services.CreateAsyncScope();
        var firstOrder = await a.ServiceProvider.GetRequiredService<IOrderStore>().FindOrder(order.Id, default);
        var secondOrder = await b.ServiceProvider.GetRequiredService<IOrderStore>().FindOrder(order.Id, default);
        await Task.WhenAll(a.ServiceProvider.GetRequiredService<IOrderCollection>().TransferDeliveredItem(firstOrder!, default),
            b.ServiceProvider.GetRequiredService<IOrderCollection>().TransferDeliveredItem(secondOrder!, default));
        Guid buyerItemId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
            var ledger = await db.OwnershipTransfers.SingleAsync(x => x.OrderId == order.Id); buyerItemId = ledger.BuyerItemId;
            var source = await db.Items.Include(x => x.Assets).SingleAsync(x => x.Id == listing.CollectibleItemId);
            var target = await db.Items.Include(x => x.Assets).SingleAsync(x => x.Id == buyerItemId);
            Assert.Equal(CollectionRules.SoldStatus, source.Status); Assert.Equal("Seller private notes", source.Notes); Assert.Single(source.Assets);
            Assert.Equal(30m, source.AcquisitionAmount); Assert.Equal(100m, target.AcquisitionAmount); Assert.Equal("BRL", target.AcquisitionCurrency);
            Assert.Null(target.Notes); Assert.Empty(target.Assets); Assert.Null(target.ListedById); Assert.Equal(buyer.User.Id, target.UserId);
            Assert.Single(await db.Items.Where(x => x.UserId == buyer.User.Id && x.Status == CollectionRules.ActiveStatus).ToArrayAsync());
        }
        var summary = (await api.GetFromJsonAsync<CollectionSummaryDto>("/api/v1/me/collection/summary"))!; Assert.Equal(1, summary.TotalItems);
        var own = (await api.GetFromJsonAsync<CollectibleItemDto>($"/api/v1/me/collection/items/{buyerItemId}"))!;
        (await api.DeleteAsync($"/api/v1/me/collection/items/{buyerItemId}?version={own.Version}")).EnsureSuccessStatusCode();
        (await api.PostAsync($"/api/v1/orders/{order.Id}/deliver", null)).EnsureSuccessStatusCode();
        summary = (await api.GetFromJsonAsync<CollectionSummaryDto>("/api/v1/me/collection/summary"))!; Assert.Equal(0, summary.TotalItems);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        summary = (await api.GetFromJsonAsync<CollectionSummaryDto>("/api/v1/me/collection/summary"))!; Assert.Equal(0, summary.TotalItems);
    }

    [Fact]
    public async Task PersistedPublicationAndCancellationCanBeRecoveredAfterInterruptedRequest()
    {
        var seller = Guid.NewGuid();
        var listing = await CollectionCommerceSeed.Listing(fixture.Factory.Services, seller);
        var now = DateTimeOffset.UtcNow; listing.PreparePublication(now);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var market = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>(); market.Listings.Add(listing); await market.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<ICollectionMarketplace>().Release(seller, listing.CollectibleItemId, listing.Id, default);
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IMarketplaceStore>();
            var pending = await store.FindListing(seller, listing.Id, default);
            await scope.ServiceProvider.GetRequiredService<ListingPublicationService>().Publish(pending!, default);
            Assert.Equal(MarketplaceRules.ActiveStatus, pending!.Status);
            pending.Cancel(pending.Version, now); await store.Save(default); // interruption before releasing Collection
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IMarketplaceStore>(); var cancelled = await store.FindListing(seller, listing.Id, default);
            await scope.ServiceProvider.GetRequiredService<IMarketplaceCollection>().ReleaseListingItem(cancelled!, default);
            Assert.Null((await scope.ServiceProvider.GetRequiredService<CollectionDbContext>().Items.SingleAsync(x => x.Id == listing.CollectibleItemId)).ListedById);
        }
    }

    private async Task<Guid> Printing()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>(); var provider = new Provider();
        await new CatalogSyncService(db, [provider], scope.ServiceProvider.GetRequiredService<IClock>(), NullLogger<CatalogSyncService>.Instance).Synchronize(provider.Code, provider.SetId, default);
        return await db.Printings.Where(x => x.Set.Name == provider.SetId).Select(x => x.Id).SingleAsync();
    }

    [Fact]
    public async Task ConcurrentBuyersReachTheDatabaseConstraintAndOnlyOneOrderWins()
    {
        using var seed = fixture.Factory.CreateClient(); var seller = await Register(seed); var firstBuyer = await Register(seed); var secondBuyer = await Register(seed);
        var listing = await CollectionCommerceSeed.Listing(fixture.Factory.Services, seller.User.Id);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>(); db.Listings.Add(listing);
            db.SellerProfiles.Add(SellerProfile.Enable(seller.User.Id, null, null, null, null, null, DateTimeOffset.UtcNow)); await db.SaveChangesAsync();
        }
        var barrier = new TwoBuyers();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOrderMarketplace>();
            services.AddScoped<IOrderMarketplace>(sp => new RacingMarketplace(new OrderMarketplaceAdapter(sp.GetRequiredService<IMarketplaceStore>(),
                sp.GetRequiredService<IClock>(), sp.GetRequiredService<IMarketplaceCollection>()), barrier));
        }));
        using var a = factory.CreateClient(); using var b = factory.CreateClient();
        a.DefaultRequestHeaders.Authorization = new("Bearer", firstBuyer.AccessToken); b.DefaultRequestHeaders.Authorization = new("Bearer", secondBuyer.AccessToken);
        var request = new CreateOrderRequest(listing.Id, null, null, null, null, null, null, null, null);
        var results = await Task.WhenAll(a.PostAsJsonAsync("/api/v1/orders", request), b.PostAsJsonAsync("/api/v1/orders", request));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var inspect = factory.Services.CreateAsyncScope();
        Assert.Single(await inspect.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.Where(x => x.ListingId == listing.Id).ToArrayAsync());
    }

    private sealed class TwoBuyers
    {
        private int _readers;
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Wait(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _readers) == 2) _both.TrySetResult();
            await _both.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        }
    }
    private sealed class RacingMarketplace(IOrderMarketplace inner, TwoBuyers barrier) : IOrderMarketplace
    {
        public async Task<ListingDto?> GetActiveListing(Guid id, CancellationToken ct)
        {
            var listing = await inner.GetActiveListing(id, ct); await barrier.Wait(ct); return listing;
        }
        public Task MarkListingAsSold(Guid id, Guid order, DateTimeOffset now, CancellationToken ct) => inner.MarkListingAsSold(id, order, now, ct);
        public Task ReleaseListing(Order order, CancellationToken ct) => inner.ReleaseListing(order, ct);
    }
    private static async Task<AuthResponse> Register(HttpClient api)
    {
        var request = new RegisterRequest($"{Guid.NewGuid():N}@example.test", "Secure-Test-Password1!", "u" + Guid.NewGuid().ToString("N")[..20], "User");
        (await api.PostAsJsonAsync("/api/v1/auth/register", request)).EnsureSuccessStatusCode();
        var login = await api.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password)); login.EnsureSuccessStatusCode(); return (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
    }
    private sealed class Provider : ICatalogProvider
    {
        public string Code => "ownership-test";
        public string SetId { get; } = Guid.NewGuid().ToString("N");
        private ProviderSet Set => new(SetId, SetId, null, null);
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken ct) => Task.FromResult<IReadOnlyList<ProviderSet>>([Set]);
        public Task<ProviderSetDetails> GetSetDetails(string id, CancellationToken ct) => Task.FromResult(new ProviderSetDetails(Set, [new(SetId + "-card", "Ownership card", "1", "en", "common", null, [])]));
    }
}
