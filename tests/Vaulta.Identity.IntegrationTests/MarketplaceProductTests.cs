using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Domain;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Marketplace.Domain;
using Vaulta.Marketplace.Infrastructure;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class MarketplaceProductTests(ApiFixture fixture)
{
    [Fact]
    public async Task ProductsAggregateBeforePagingAndDoNotResolveSellerPhotos()
    {
        var s = await Seed(); using var api = fixture.Factory.CreateClient();
        var first = await Read(api, $"/products?game={s.Game}&pageSize=2&sort=price_asc");
        var second = await Read(api, $"/products?game={s.Game}&pageSize=2&page=2&sort=price_asc");
        Assert.Equal(4, first.GetProperty("totalCount").GetInt32());
        var items = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToArray();
        Assert.Equal(new[] { 50m, 60m, 90m, 120m }, items.Select(x => x.GetProperty("lowestPriceBrl").GetDecimal()));
        var holo = Assert.Single(items, x => x.GetProperty("variantId").ValueKind != JsonValueKind.Null && x.GetProperty("variantId").GetGuid() == s.Holo);
        Assert.Equal(2, holo.GetProperty("offerCount").GetInt32());
        Assert.Equal(s.Printing, holo.GetProperty("printingId").GetGuid());
        Assert.Equal("en", holo.GetProperty("printing").GetProperty("language").GetString());
        Assert.False(holo.TryGetProperty("photos", out _));
    }

    [Fact]
    public async Task ExactNoneVariantDoesNotMixOrCountOtherFinishes()
    {
        var s = await Seed(); using var api = fixture.Factory.CreateClient();
        var product = await Read(api, $"/products/{s.Printing}/variants/none");
        Assert.Equal(1, product.GetProperty("offerCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, product.GetProperty("marketQuote").ValueKind);
        var offers = await Read(api, $"/products/{s.Printing}/variants/none/offers?pageSize=1");
        Assert.Equal(1, offers.GetProperty("totalCount").GetInt32());
        Assert.Equal(s.NullListing, Assert.Single(offers.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        using var mismatch = await api.GetAsync($"/api/v1/marketplace/products/{s.OtherPrinting}/variants/{s.Holo}");
        Assert.Equal(HttpStatusCode.NotFound, mismatch.StatusCode);
    }

    [Fact]
    public async Task ProductUsesExactStoredBrlQuoteWithProvenance()
    {
        var s = await Seed(); using var api = fixture.Factory.CreateClient();
        var product = await Read(api, $"/products/{s.Printing}/variants/{s.Holo}");
        var quote = product.GetProperty("marketQuote");
        Assert.Equal(99.95m, quote.GetProperty("marketValueBrl").GetDecimal());
        Assert.Equal("TCGplayer", quote.GetProperty("source").GetString());
        Assert.Equal("USD", quote.GetProperty("originalCurrency").GetString());
        Assert.Equal(20m, quote.GetProperty("originalValue").GetDecimal());
        Assert.Equal(4.9975m, quote.GetProperty("exchangeRate").GetDecimal());
        var offers = await Read(api, $"/products/{s.Printing}/variants/{s.Holo}/offers?pageSize=1&sort=price_asc");
        Assert.Equal(2, offers.GetProperty("totalCount").GetInt32());
        Assert.Equal(90m, Assert.Single(offers.GetProperty("items").EnumerateArray()).GetProperty("priceBrl").GetDecimal());
    }

    [Fact]
    public async Task FiltersApplyBeforeMinimumAndCountAndInvalidRangesAreRejected()
    {
        var s = await Seed(); using var api = fixture.Factory.CreateClient();
        var result = await Read(api, $"/products?game={s.Game}&setId={s.Set}&language=en&variantCode=holo&condition=NEAR_MINT&minPriceBrl=95&maxPriceBrl=110");
        var item = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal(100m, item.GetProperty("lowestPriceBrl").GetDecimal());
        Assert.Equal(1, item.GetProperty("offerCount").GetInt32());
        var empty = await Read(api, $"/products?game={s.Game}&query=notfound");
        Assert.Equal(0, empty.GetProperty("totalCount").GetInt32());
        using var invalid = await api.GetAsync("/api/v1/marketplace/products?minPriceBrl=20&maxPriceBrl=10");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task InactiveVariantsDoNotAppearOrAffectProductPagination()
    {
        var s = await Seed();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var variant = await catalog.Variants.FindAsync(s.Holo);
            variant!.IsActive = false;
            await catalog.SaveChangesAsync();
        }
        using var api = fixture.Factory.CreateClient();
        var result = await Read(api, $"/products?game={s.Game}&pageSize=2&sort=price_asc");
        Assert.Equal(3, result.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain(result.GetProperty("items").EnumerateArray(), x => x.GetProperty("variantId").ValueKind != JsonValueKind.Null && x.GetProperty("variantId").GetGuid() == s.Holo);
    }

    [Fact]
    public async Task IncompleteStoredQuoteDoesNotBreakProductBrowsing()
    {
        var s = await Seed();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catalog.DailyMarketSnapshots.Add(new() { PrintingId=s.Printing, MarketDay=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), FetchedAt=DateTimeOffset.UtcNow, RefreshAfter=DateTimeOffset.UtcNow.AddDays(2), Payload="{\"Printing\":null,\"MarketQuotes\":null}" });
            await catalog.SaveChangesAsync();
        }
        using var api = fixture.Factory.CreateClient();
        var product = await Read(api, $"/products/{s.Printing}/variants/{s.Holo}");
        Assert.Equal(2, product.GetProperty("offerCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, product.GetProperty("marketQuote").ValueKind);
    }

    [Fact]
    public async Task RegionalLanguageFilterMatchesTheCatalogOption()
    {
        var s = await Seed();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var printing = await catalog.Printings.FindAsync(s.OtherPrinting);
            printing!.Language = "pt-BR";
            await catalog.SaveChangesAsync();
        }
        using var api = fixture.Factory.CreateClient();
        var result = await Read(api, $"/products?game={s.Game}&language=pt-BR");
        Assert.Equal(s.OtherPrinting, Assert.Single(result.GetProperty("items").EnumerateArray()).GetProperty("printingId").GetGuid());
    }

    [Fact]
    public async Task FilterOptionsUseSearchablePagedSetsAndActualCatalogLanguagesAndFinishes()
    {
        var s = await Seed(); using var api = fixture.Factory.CreateClient();
        var options = await Read(api, $"/product-filters?game={s.Game}&setQuery=flames&pageSize=1");
        Assert.Equal(1, options.GetProperty("setTotalCount").GetInt32());
        Assert.Equal(s.Set, Assert.Single(options.GetProperty("sets").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(new[] {"en","pt"}, options.GetProperty("languages").EnumerateArray().Select(x=>x.GetString()));
        Assert.Equal(new[] {"holo","reverse"}, options.GetProperty("variantCodes").EnumerateArray().Select(x=>x.GetString()));
    }

    private static async Task<JsonElement> Read(HttpClient api, string path)
    {
        using var response = await api.GetAsync("/api/v1/marketplace" + path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private async Task<SeedData> Seed()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var market = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var game = new Game { Id = Guid.NewGuid(), Code = "product-" + Guid.NewGuid().ToString("N")[..16], Name = "Product tests" };
        var set = new Set { Id = Guid.NewGuid(), Game = game, Name = "Test flames", NormalizedName = "test flames" };
        var card = new Card { Id = Guid.NewGuid(), Game = game, Name = "Charizard ex", NormalizedName = "charizard ex" };
        var printing = new Printing { Id = Guid.NewGuid(), Card = card, Set = set, CollectorNumber = "223/197", NormalizedCollectorNumber = "223/197", Language = "en" };
        var other = new Printing { Id = Guid.NewGuid(), Card = card, Set = set, CollectorNumber = "223/197", NormalizedCollectorNumber = "223/197", Language = "pt" };
        var holo = new Variant { Id = Guid.NewGuid(), Printing = printing, Code = "holo", Name = "Holo" };
        var reverse = new Variant { Id = Guid.NewGuid(), Printing = printing, Code = "reverse", Name = "Reverse" };
        catalog.Variants.AddRange(holo, reverse); catalog.Printings.Add(other);
        var now = DateTimeOffset.UtcNow;
        var quotes = new[] { new CardMarketQuoteDto(holo.Id, "Holo", 99.95m, 20, "USD", "TCGplayer", now, 4.9975m, now, []) };
        catalog.DailyMarketSnapshots.Add(new() { PrintingId = printing.Id, MarketDay = DateOnly.FromDateTime(now.UtcDateTime), FetchedAt = now, RefreshAfter = now.AddDays(1), Payload = JsonSerializer.Serialize(new ScannerCardDetailsDto(new(printing.Id, card.Id, set.Id, game.Code, set.Name, card.Name, "223/197", "en", null, null, [new(holo.Id,"holo","Holo")]), new Dictionary<string,string>(), quotes, null)) });
        await catalog.SaveChangesAsync();
        var seller = Guid.NewGuid(); var suspended = Guid.NewGuid();
        market.SellerProfiles.Add(SellerProfile.Enable(seller, null, null, null, null, null, now));
        var profile = SellerProfile.Enable(suspended, null, null, null, null, null, now); profile.Suspend("test", now); market.SellerProfiles.Add(profile);
        Listing Make(Guid p, Guid? v, decimal price, string condition = "NEAR_MINT", Guid? owner = null) => Listing.Create(owner ?? seller, Guid.NewGuid(), p, v, condition, price, null, now);
        var front = Make(printing.Id, holo.Id, 100); front.AddPhoto(Guid.NewGuid(), "FRONT", 0, now);
        var cancelled = Make(printing.Id, holo.Id, 20); cancelled.Cancel(cancelled.Version, now);
        var plain = Make(printing.Id, null, 120);
        market.Listings.AddRange(front, Make(printing.Id, holo.Id, 90, "LIGHTLY_PLAYED"), Make(printing.Id, reverse.Id, 60), plain, Make(other.Id, null, 50), cancelled, Make(printing.Id, holo.Id, 10, owner:suspended));
        await market.SaveChangesAsync();
        return new(game.Code, set.Id, printing.Id, other.Id, holo.Id, plain.Id);
    }
    private sealed record SeedData(string Game, Guid Set, Guid Printing, Guid OtherPrinting, Guid Holo, Guid NullListing);
}
