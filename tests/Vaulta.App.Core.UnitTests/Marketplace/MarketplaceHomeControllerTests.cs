using System.Net;
using System.Net.Http.Json;
using Vaulta.App.Core.Marketplace;
using Vaulta.Marketplace.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Marketplace;

public sealed class MarketplaceHomeControllerTests
{
    [Fact]
    public async Task RefreshShowsLoadingThenRealListings()
    {
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new BrowseHandler((_, _) => reply.Task);
        using var controller = Controller(handler);
        var listing = Listing();
        var pending = controller.RefreshAsync();
        Assert.True(controller.State.IsLoading);
        Assert.False(controller.State.IsEmpty);
        reply.SetResult(Page([listing], 1, 1));
        await pending;
        Assert.Equal(listing.Id, Assert.Single(controller.State.Items).Id);
        Assert.False(controller.State.IsBusy);
        Assert.True(controller.State.HasLoaded);
    }

    [Fact]
    public async Task FiltersResetPaginationAndAreSentToBrowse()
    {
        using var handler = new BrowseHandler((request, _) => Task.FromResult(Page([Listing()], 1, 100)));
        using var controller = Controller(handler);
        await controller.RefreshAsync();
        await controller.LoadMoreAsync();
        await controller.RefreshAsync(new("  Charizard & ex  ", "pokemon", "price_asc"));
        Assert.Equal(1, controller.State.Page);
        Assert.Single(controller.State.Items);
        Assert.Equal("Charizard & ex", controller.State.Filters.Query);
        var uri = handler.Requests[^1].AbsoluteUri;
        Assert.Contains("query=Charizard%20%26%20ex", uri);
        Assert.Contains("game=pokemon", uri);
        Assert.Contains("page=1", uri);
        Assert.Contains("sort=price_asc", uri);
    }

    [Fact]
    public async Task PaginationAppendsDistinctListingsAndStopsAtEnd()
    {
        var first = Listing(); var second = Listing();
        using var handler = new BrowseHandler((_, _) => Task.FromResult(
            Page(handlerPage++ == 0 ? [first] : [first, second], handlerPage, 2)));
        using var controller = Controller(handler);
        await controller.RefreshAsync();
        await controller.LoadMoreAsync();
        await controller.LoadMoreAsync();
        Assert.Equal(new[] { first.Id, second.Id }, controller.State.Items.Select(item => item.Id));
        Assert.False(controller.State.HasMore);
        Assert.Equal(2, handler.Requests.Count);
    }
    private int handlerPage;

    [Fact]
    public async Task SellerFilterPersistsAcrossPagination()
    {
        var seller = Guid.NewGuid();
        using var handler = new BrowseHandler((_, _) => Task.FromResult(Page([Listing()], 1, 100)));
        using var controller = Controller(handler);
        await controller.RefreshAsync(new(null, null, "newest", seller));
        await controller.LoadMoreAsync();
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.Contains("sellerUserId=" + seller, uri.Query));
        Assert.Contains("page=2", handler.Requests[1].Query);
    }

    [Fact]
    public async Task ReplacedSearchCancelsRequestAndRejectsItsLateSuccess()
    {
        var oldReply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        var latest = Listing();
        using var handler = new BrowseHandler((request, token) =>
        {
            if (request.RequestUri!.Query.Contains("query=old")) { oldToken = token; return oldReply.Task; }
            return Task.FromResult(Page([latest], 1, 1));
        });
        using var controller = Controller(handler);
        var old = controller.RefreshAsync(new("old", "pokemon", "newest"));
        await controller.RefreshAsync(new("new", "pokemon", "newest"));
        Assert.True(oldToken.IsCancellationRequested);
        oldReply.SetResult(Page([Listing()], 1, 1));
        await old;
        Assert.Equal(latest.Id, Assert.Single(controller.State.Items).Id);
        Assert.Equal("new", controller.State.Filters.Query);
    }

    [Fact]
    public async Task ReplacedSearchRejectsLateFailure()
    {
        var oldReply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new BrowseHandler((request, _) => request.RequestUri!.Query.Contains("query=old")
            ? oldReply.Task : Task.FromResult(Page([], 1, 0)));
        using var controller = Controller(handler);
        var old = controller.RefreshAsync(new("old", null, "newest"));
        await controller.RefreshAsync(new("new", null, "newest"));
        oldReply.SetException(new HttpRequestException("internal transport detail"));
        await old;
        Assert.Equal(MarketplaceHomeProblem.None, controller.State.Problem);
        Assert.True(controller.State.IsEmpty);
    }

    [Fact]
    public async Task OfflineShowsRecoverableStateWithoutRequestingNetwork()
    {
        var connected = false;
        using var handler = new BrowseHandler((_, _) => Task.FromResult(Page([], 1, 0)));
        using var controller = Controller(handler, () => connected);
        await controller.RefreshAsync();
        Assert.Equal(MarketplaceHomeProblem.Offline, controller.State.Problem);
        Assert.Empty(handler.Requests);
        Assert.False(controller.State.IsEmpty);
        connected = true;
        await controller.RetryAsync();
        Assert.True(controller.State.IsEmpty);
        Assert.Equal(MarketplaceHomeProblem.None, controller.State.Problem);
    }

    [Fact]
    public async Task FailedNextPageKeepsItemsAndRetryRequestsSamePage()
    {
        var listing = Listing(); var calls = 0;
        using var handler = new BrowseHandler((_, _) => Task.FromResult(++calls == 2
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Page([listing], calls == 1 ? 1 : 2, calls == 1 ? 2 : 1)));
        using var controller = Controller(handler);
        await controller.RefreshAsync();
        await controller.LoadMoreAsync();
        Assert.Equal(listing.Id, Assert.Single(controller.State.Items).Id);
        Assert.Equal(1, controller.State.Page);
        Assert.Equal(MarketplaceHomeProblem.Error, controller.State.Problem);
        await controller.RetryAsync();
        Assert.Equal(2, controller.State.Page);
        Assert.Contains("page=2", handler.Requests[^1].Query);
        Assert.Equal(MarketplaceHomeProblem.None, controller.State.Problem);
    }

    [Fact]
    public async Task EmptyResponseIsEmptyInsteadOfError()
    {
        using var handler = new BrowseHandler((_, _) => Task.FromResult(Page([], 1, 0)));
        using var controller = Controller(handler);
        await controller.RefreshAsync();
        Assert.True(controller.State.IsEmpty);
        Assert.False(controller.State.HasMore);
        Assert.Equal(MarketplaceHomeProblem.None, controller.State.Problem);
    }

    [Fact]
    public async Task LeavingCancelsRequestWithoutPresentingError()
    {
        using var handler = new BrowseHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Page([], 1, 0);
        });
        using var controller = Controller(handler);
        var pending = controller.RefreshAsync();
        controller.CancelPending();
        await pending;
        Assert.False(controller.State.IsBusy);
        Assert.Equal(MarketplaceHomeProblem.None, controller.State.Problem);
        Assert.False(controller.State.HasLoaded);
    }

    private static MarketplaceHomeController Controller(HttpMessageHandler handler, Func<bool>? connected = null) =>
        new(new MarketplaceClient(new HttpClient(handler) { BaseAddress = new("https://api.test/") }), connected);
    private static ListingDto Listing() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
        "NM", 100m, "BRL", "active", null, [], DateTimeOffset.UtcNow, Guid.NewGuid());
    private static HttpResponseMessage Page(IReadOnlyList<ListingDto> listings, int page, int total) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new ListingPageDto(listings, page, 20, total)) };
    private sealed class BrowseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!);
            return reply(request, token);
        }
    }
}
