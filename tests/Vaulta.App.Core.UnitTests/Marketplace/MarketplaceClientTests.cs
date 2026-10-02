using System.Net;
using System.Net.Http.Json;
using Vaulta.App.Core.Marketplace;
using Vaulta.App.Core.UnitTests.TestSupport;
using Vaulta.Marketplace.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Marketplace;

public sealed class MarketplaceClientTests
{
    private static MarketplaceClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });

    [Fact]
    public async Task BrowseEncodesQueryAndAllFilters()
    {
        Uri? seen = null;
        var client = Client(new FakeHttpMessageHandler(request =>
        {
            seen = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ListingPageDto([], 2, 7, 0)) };
        }));
        var seller = Guid.NewGuid(); var printing = Guid.NewGuid(); var variant = Guid.NewGuid();
        await client.BrowseListingsAsync(new(seller, printing, variant, "Pokémon & cartas", "pokemon", 2, 7, "price_asc"));
        var url = seen!.AbsoluteUri;
        Assert.Contains("query=" + Uri.EscapeDataString("Pokémon & cartas"), url);
        Assert.Contains("game=pokemon", url);
        Assert.Contains("sellerUserId=" + seller, url);
        Assert.Contains("printingId=" + printing, url);
        Assert.Contains("variantId=" + variant, url);
        Assert.Contains("page=2", url);
        Assert.Contains("pageSize=7", url);
        Assert.Contains("sort=price_asc", url);
    }

    [Fact]
    public async Task LegacyListKeepsDefaults()
    {
        Uri? seen = null;
        var client = Client(new FakeHttpMessageHandler(request =>
        {
            seen = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ListingPageDto([], 1, 20, 0)) };
        }));
        await client.ListActiveListingsAsync();
        Assert.Equal("https://api.test/api/v1/marketplace/listings?page=1&pageSize=20&sort=newest", seen!.AbsoluteUri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowseDeserializesOptionalPrinting(bool enriched)
    {
        var listing = new ListingDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            "NM", 100, "BRL", "active", null, [], DateTimeOffset.UtcNow, Guid.NewGuid(), 0, 0,
            enriched ? new("Charizard", "Flames", "223/197", "en", "pokemon", null, null) : null);
        var json = System.Text.Json.JsonSerializer.Serialize(new ListingPageDto([listing], 1, 20, 1),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (!enriched) json = json.Replace(",\"printing\":null", "");
        var client = Client(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") }));
        var result = await client.BrowseListingsAsync(new(null, null, null, null, null, 1, 20, "newest"));
        Assert.Equal(listing.Id, Assert.Single(result.Items).Id);
        Assert.Equal(enriched ? "Charizard" : null, result.Items[0].Printing?.CardName);
        Assert.Equal(100m, result.Items[0].PriceBrl);
    }

    [Fact]
    public async Task BrowsePropagatesCancellation()
    {
        using var handler = new CancellationHandler();
        var client = Client(handler);
        using var cancellation = new CancellationTokenSource();
        var pending = client.BrowseListingsAsync(new(null, null, null, null, null, 1, 20, "newest"), cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new(HttpStatusCode.OK);
        }
    }
}

