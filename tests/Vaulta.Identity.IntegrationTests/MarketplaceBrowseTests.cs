using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Domain;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Marketplace.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.Marketplace.Infrastructure;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class MarketplaceBrowseTests(ApiFixture fixture)
{
    [Fact]
    public async Task BrowseReturnsPrintingMetadataAndLegacyFields()
    {
        var seed = await Seed();
        using var api = fixture.Factory.CreateClient();
        var page = await Browse(api, seed.Seller);
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(seed.Listings[0].Id, item.GetProperty("id").GetGuid());
        Assert.Equal(100m, item.GetProperty("priceBrl").GetDecimal());
        Assert.Equal("BRL", item.GetProperty("currency").GetString());
        Assert.Empty(item.GetProperty("photos").EnumerateArray());
        Assert.Equal(0, item.GetProperty("sellerTotalReviews").GetInt32());
        var printing = item.GetProperty("printing");
        Assert.Equal(seed.CardName, printing.GetProperty("cardName").GetString());
        Assert.Equal(seed.SetName, printing.GetProperty("setName").GetString());
        Assert.Equal("223/197", printing.GetProperty("collectorNumber").GetString());
        Assert.Equal("en", printing.GetProperty("language").GetString());
        Assert.Equal(seed.Game, printing.GetProperty("gameCode").GetString());
        Assert.Equal("https://images.example.test/card.png", printing.GetProperty("artworkUrl").GetString());
        Assert.Equal("holo", printing.GetProperty("variantCode").GetString());
    }

    [Fact]
    public async Task BrowseNoMatchReturnsEmpty()
    {
        var seed = await Seed();
        using var api = fixture.Factory.CreateClient();
        var page = await Browse(api, seed.Seller, "&query=notfound" + Guid.NewGuid().ToString("N"));
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task BrowseFiltersGameWithoutQuery()
    {
        var seed = await Seed(twoGames: true);
        using var api = fixture.Factory.CreateClient();
        var page = await Browse(api, seed.Seller, "&query=%20%20&game=" + seed.Game);
        Assert.Equal(seed.Listings[0].Id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task BrowseSearchesCardSetAndNumber()
    {
        var seed = await Seed(twoGames: true);
        using var api = fixture.Factory.CreateClient();
        foreach (var query in new[] { seed.CardName, seed.SetName, "223/197" })
        {
            var page = await Browse(api, seed.Seller, "&query=" + Uri.EscapeDataString(query) + "&game=" + seed.Game);
            Assert.Equal(seed.Listings[0].Id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }
    }

    [Fact]
    public async Task BrowseAppliesExistingFilters()
    {
        var seed = await Seed(copies: 2);
        using var api = fixture.Factory.CreateClient();
        var page = await Browse(api, seed.Seller, $"&query={Uri.EscapeDataString(seed.CardName)}&printingId={seed.Printing}&variantId={seed.Variant}");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        var empty = await Browse(api, seed.Seller, $"&query={Uri.EscapeDataString(seed.CardName)}&variantId={Guid.NewGuid()}");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("newest", true)]
    [InlineData("price_asc", false)]
    [InlineData("price_desc", true)]
    public async Task BrowseOrdersTiesDeterministically(string sort, bool descending)
    {
        var seed = await Seed(copies: 4);
        using var api = fixture.Factory.CreateClient();
        var expected = descending ? seed.Listings.OrderByDescending(x => x.Id) : seed.Listings.OrderBy(x => x.Id);
        var first = await Browse(api, seed.Seller, $"&sort={sort}&pageSize=2");
        var second = await Browse(api, seed.Seller, $"&sort={sort}&pageSize=2&page=2");
        var actual = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(x => x.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(expected.Select(x => x.Id), actual);
    }

    [Fact]
    public async Task BrowseRetainsListingWithoutMetadata()
    {
        var seed = await Seed();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var catalog = new CountingCatalog(scope.ServiceProvider.GetRequiredService<ICatalogCollectionReader>()) { OmitMetadata = true };
        await using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<ICatalogCollectionReader>(catalog))));
        using var api = factory.CreateClient();
        var item = Assert.Single((await Browse(api, seed.Seller)).GetProperty("items").EnumerateArray());
        Assert.Equal(seed.Listings[0].Id, item.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("printing").ValueKind);
    }

    [Fact]
    public async Task BrowseBatchesDistinctIds()
    {
        var seed = await Seed(copies: 3);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var catalog = new CountingCatalog(scope.ServiceProvider.GetRequiredService<ICatalogCollectionReader>());
        await using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<ICatalogCollectionReader>(catalog))));
        using var api = factory.CreateClient();
        Assert.Equal(3, (await Browse(api, seed.Seller)).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, catalog.PrintingBatches);
        Assert.Equal(1, catalog.VariantBatches);
        Assert.Equal(0, catalog.SingleCalls);
        Assert.Equal(new[] { seed.Printing }, catalog.PrintingIds);
        Assert.Equal(new[] { seed.Variant }, catalog.VariantIds);
    }

    [Fact]
    public async Task BrowseExcludesSuspendedSellersAndInactiveListings()
    {
        var seed = await Seed(copies: 2);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var cancelled = await db.Listings.FindAsync(seed.Listings[1].Id);
        cancelled!.Cancel(cancelled.Version, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        using var api = fixture.Factory.CreateClient();
        Assert.Single((await Browse(api, seed.Seller)).GetProperty("items").EnumerateArray());
        var seller = await db.SellerProfiles.FindAsync(seed.Seller);
        seller!.Suspend("test", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        Assert.Empty((await Browse(api, seed.Seller)).GetProperty("items").EnumerateArray());
    }

    private static async Task<JsonElement> Browse(HttpClient api, Guid seller, string filters = "")
    {
        using var response = await api.GetAsync($"/api/v1/marketplace/listings?sellerUserId={seller}{filters}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private async Task<SeedData> Seed(int copies = 1, bool twoGames = false)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..16];
        var game = new Game { Id = Guid.NewGuid(), Code = "browse-" + suffix, Name = "Browse game" };
        var set = new Set { Id = Guid.NewGuid(), Game = game, Name = "Flames " + suffix, NormalizedName = "flames " + suffix };
        var card = new Card { Id = Guid.NewGuid(), Game = game, Name = "Charizard " + suffix, NormalizedName = "charizard " + suffix };
        var printing = new Printing { Id = Guid.NewGuid(), Card = card, Set = set, CollectorNumber = "223/197", NormalizedCollectorNumber = "223/197", Language = "en", ExternalArtworkUrl = "https://images.example.test/card.png" };
        var variant = new Variant { Id = Guid.NewGuid(), Printing = printing, Code = "holo", Name = "Holo" };
        db.Variants.Add(variant);
        Printing? other = null;
        if (twoGames)
        {
            var another = new Game { Id = Guid.NewGuid(), Code = "other-" + suffix, Name = "Other game" };
            var otherSet = new Set { Id = Guid.NewGuid(), Game = another, Name = "Other set", NormalizedName = "other set" };
            var otherCard = new Card { Id = Guid.NewGuid(), Game = another, Name = "Other card", NormalizedName = "other card" };
            other = new Printing { Id = Guid.NewGuid(), Set = otherSet, Card = otherCard, CollectorNumber = "1", NormalizedCollectorNumber = "1", Language = "en" };
            db.Printings.Add(other);
        }
        await db.SaveChangesAsync();
        var market = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var seller = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        market.SellerProfiles.Add(SellerProfile.Enable(seller, null, null, null, null, null, now));
        var listings = Enumerable.Range(0, copies).Select(_ => Listing.Create(seller, Guid.NewGuid(), printing.Id, variant.Id, "NM", 100, null, now)).ToList();
        if (other is not null) listings.Add(Listing.Create(seller, Guid.NewGuid(), other.Id, null, "LP", 150, null, now));
        market.Listings.AddRange(listings);
        await market.SaveChangesAsync();
        return new(seller, printing.Id, variant.Id, game.Code, card.Name, set.Name, listings);
    }

    private sealed record SeedData(Guid Seller, Guid Printing, Guid Variant, string Game, string CardName, string SetName, IReadOnlyList<Listing> Listings);

    private sealed class CountingCatalog(ICatalogCollectionReader inner) : ICatalogCollectionReader
    {
        public bool OmitMetadata { get; init; }
        public int PrintingBatches { get; private set; }
        public int VariantBatches { get; private set; }
        public int SingleCalls { get; private set; }
        public Guid[] PrintingIds { get; private set; } = [];
        public Guid[] VariantIds { get; private set; } = [];
        public Task<CollectionPrintingDetails?> GetPrinting(Guid id, CancellationToken ct) { SingleCalls++; return inner.GetPrinting(id, ct); }
        public Task<CollectionVariantDetails?> GetVariant(Guid id, CancellationToken ct) { SingleCalls++; return inner.GetVariant(id, ct); }
        public Task<IReadOnlyList<Guid>> SearchPrintingIds(string? query, string? game, Guid? set, CancellationToken ct) => inner.SearchPrintingIds(query, game, set, ct);
        public Task<IReadOnlyList<CollectionPrintingDetails>> GetPrintings(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        {
            PrintingBatches++; PrintingIds = ids.ToArray();
            return OmitMetadata ? Task.FromResult<IReadOnlyList<CollectionPrintingDetails>>([]) : inner.GetPrintings(ids, ct);
        }
        public Task<IReadOnlyList<CollectionVariantDetails>> GetVariants(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        {
            VariantBatches++; VariantIds = ids.ToArray(); return inner.GetVariants(ids, ct);
        }
    }
}

