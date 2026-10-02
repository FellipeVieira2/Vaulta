using System.Net;
using System.Net.Http.Json;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Marketplace;
using Vaulta.Catalog.Contracts;
using Vaulta.Marketplace.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Marketplace;

public sealed class MarketplaceListingDetailControllerTests
{
    [Fact]
    public async Task LoadShowsLoadingThenRealListing()
    {
        var listing = Listing();
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transport = new Transport((request, _) => IsBrowse(request) ? Task.FromResult(Page([], 1, 0)) : reply.Task);
        using var controller = Controller(transport);
        var pending = controller.LoadAsync(listing.Id);
        Assert.True(controller.State.IsLoading);
        reply.SetResult(Json(listing));
        await pending;
        Assert.Equal(listing.Id, controller.State.Listing?.Id);
        Assert.False(controller.State.IsLoading);
        Assert.False(controller.State.IsComparing);
        Assert.True(controller.State.CanBuy);
    }

    [Fact]
    public async Task MissingListingDoesNotRequestComparison()
    {
        using var transport = new Transport((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var controller = Controller(transport);
        await controller.LoadAsync(Guid.NewGuid());
        Assert.True(controller.State.NotFound);
        Assert.Null(controller.State.Listing);
        Assert.False(controller.State.CanBuy);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task ComparisonsMatchPrintingAndVariantAndUsePriceSort()
    {
        var listing = Listing(); var compatible = Listing() with { PrintingId = listing.PrintingId, VariantId = listing.VariantId };
        var otherPrinting = Listing() with { VariantId = listing.VariantId };
        var otherVariant = compatible with { Id = Guid.NewGuid(), VariantId = Guid.NewGuid() };
        using var transport = new Transport((request, _) => Task.FromResult(IsBrowse(request)
            ? Page([compatible, otherPrinting, otherVariant], 1, 3) : Json(listing)));
        using var controller = Controller(transport);
        await controller.LoadAsync(listing.Id);
        Assert.Equal(compatible.Id, Assert.Single(controller.State.Comparisons).Id);
        var query = transport.Requests.Single(uri => uri.AbsolutePath.EndsWith("/listings")).Query;
        Assert.Contains("printingId=" + listing.PrintingId, query);
        Assert.Contains("variantId=" + listing.VariantId, query);
        Assert.Contains("sort=price_asc", query);
        Assert.DoesNotContain("sellerUserId", query);
    }

    [Fact]
    public async Task NullVariantDoesNotCompareOtherVariant()
    {
        var listing = Listing() with { VariantId = null };
        var different = listing with { Id = Guid.NewGuid(), VariantId = Guid.NewGuid() };
        using var transport = new Transport((request, _) => Task.FromResult(IsBrowse(request) ? Page([different], 1, 1) : Json(listing)));
        using var controller = Controller(transport);
        await controller.LoadAsync(listing.Id);
        Assert.Empty(controller.State.Comparisons);
    }

    [Fact]
    public async Task ComparisonFailureKeepsListingAndCanRetry()
    {
        var listing = Listing(); var calls = 0;
        using var transport = new Transport((request, _) => Task.FromResult(!IsBrowse(request) ? Json(listing)
            : ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Page([listing], 1, 1)));
        using var controller = Controller(transport);
        await controller.LoadAsync(listing.Id);
        Assert.Equal(listing.Id, controller.State.Listing?.Id);
        Assert.NotNull(controller.State.ComparisonMessage);
        Assert.Null(controller.State.Message);
        Assert.True(controller.State.CanBuy);
        await controller.RetryComparisonsAsync();
        Assert.Null(controller.State.ComparisonMessage);
        Assert.Single(controller.State.Comparisons);
    }

    [Fact]
    public async Task ComparisonTimeoutIsRecoverableWithoutHidingListing()
    {
        var listing = Listing();
        using var transport = new Transport((request, _) => IsBrowse(request)
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout")) : Task.FromResult(Json(listing)));
        using var controller = Controller(transport);
        await controller.LoadAsync(listing.Id);
        Assert.Equal(listing.Id, controller.State.Listing?.Id);
        Assert.NotNull(controller.State.ComparisonMessage);
        Assert.Null(controller.State.Message);
        Assert.True(controller.State.CanBuy);
    }

    [Fact]
    public async Task CatalogTimeoutKeepsLegacyListingAndRequestedPrice()
    {
        var listing = Listing() with { Printing = null };
        using var transport = new Transport((request, _) => request.RequestUri!.AbsolutePath.Contains("/catalog/")
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout"))
            : Task.FromResult(IsBrowse(request) ? Page([], 1, 0) : Json(listing)));
        using var controller = Controller(transport);
        await controller.LoadAsync(listing.Id);
        Assert.Equal(289.90m, controller.State.Listing?.PriceBrl);
        Assert.NotNull(controller.State.MetadataMessage);
        Assert.Null(controller.State.Message);
        Assert.True(controller.State.CanBuy);
    }

    [Fact]
    public async Task LegacyListingUsesCatalogMetadataWithoutChangingPrice()
    {
        var listing = Listing() with { Printing = null };
        var metadata = new CatalogPrintingDetails(listing.PrintingId, Guid.NewGuid(), Guid.NewGuid(), "pokemon",
            "Obsidian Flames", "Charizard ex", "223/197", "en", null, "https://catalog.test/charizard.webp", []);
        using var transport = new Transport((request, _) => Task.FromResult(IsBrowse(request) ? Page([], 1, 0)
            : request.RequestUri!.AbsolutePath.Contains("/catalog/") ? Json(metadata) : Json(listing)));
        using var controller = Controller(transport);
        await controller.LoadAsync(listing.Id);
        Assert.Equal("Charizard ex", controller.State.Listing?.Printing?.CardName);
        Assert.Equal(289.90m, controller.State.Listing?.PriceBrl);
        Assert.Null(controller.State.MetadataMessage);
    }

    [Fact]
    public async Task MissingCatalogMetadataLeavesRealListingAvailable()
    {
        var listing = Listing() with { Printing = null };
        using var transport = new Transport((request, _) => Task.FromResult(IsBrowse(request) ? Page([], 1, 0)
            : request.RequestUri!.AbsolutePath.Contains("/catalog/") ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(listing)));
        using var controller = Controller(transport);
        await controller.LoadAsync(listing.Id);
        Assert.Equal(289.90m, controller.State.Listing?.PriceBrl);
        Assert.Null(controller.State.Listing?.Printing);
        Assert.NotNull(controller.State.MetadataMessage);
        Assert.True(controller.State.CanBuy);
    }

    [Fact]
    public async Task LaterListingRejectsOldResponse()
    {
        var oldListing = Listing(); var latest = Listing();
        var oldReply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transport = new Transport((request, _) => request.RequestUri!.AbsolutePath.EndsWith(oldListing.Id.ToString())
            ? oldReply.Task : Task.FromResult(IsBrowse(request) ? Page([], 1, 0) : Json(latest)));
        using var controller = Controller(transport);
        var old = controller.LoadAsync(oldListing.Id);
        await controller.LoadAsync(latest.Id);
        oldReply.SetResult(Json(oldListing));
        await old;
        Assert.Equal(latest.Id, controller.State.Listing?.Id);
    }

    [Fact]
    public async Task PaginatedComparisonKeepsFilterAndAppends()
    {
        var listing = Listing(); var next = listing with { Id = Guid.NewGuid(), SellerUserId = Guid.NewGuid() };
        var page = 0;
        using var transport = new Transport((request, _) => Task.FromResult(IsBrowse(request)
            ? Json(new ListingPageDto(++page == 1 ? [listing] : [next], page, 1, 2)) : Json(listing)));
        using var controller = Controller(transport);
        await controller.LoadAsync(listing.Id);
        Assert.True(controller.State.HasMoreComparisons);
        await controller.LoadMoreComparisonsAsync();
        Assert.Equal(new[] { listing.Id, next.Id }, controller.State.Comparisons.Select(item => item.Id));
        Assert.False(controller.State.HasMoreComparisons);
        Assert.Contains("page=2", transport.Requests[^1].Query);
    }

    private static MarketplaceListingDetailController Controller(HttpMessageHandler transport)
    {
        var client = new HttpClient(transport) { BaseAddress = new("https://api.test/") };
        return new(new MarketplaceClient(client), new CatalogClient(client));
    }
    private static ListingDto Listing() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "NM", 289.90m, "BRL", "active", null, [], DateTimeOffset.UtcNow, Guid.NewGuid(),
        Printing: new("Charizard ex", "Obsidian Flames", "223/197", "en", "pokemon", null, "foil"));
    private static bool IsBrowse(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.EndsWith("/listings");
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private static HttpResponseMessage Page(IReadOnlyList<ListingDto> items, int page, int total) => Json(new ListingPageDto(items, page, 20, total));
    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!);
            return reply(request, token);
        }
    }
}
