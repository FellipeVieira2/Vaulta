using System.Net;
using Vaulta.App.Core.Http;
using Vaulta.App.Core.UnitTests.TestSupport;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Http;

public sealed class AuthorizingHttpMessageHandlerTests
{
    private static HttpRequestMessage NewRequest() => new(HttpMethod.Get, "https://api.test/api/v1/me/collection");

    [Fact]
    public async Task SendAsync_AttachesBearerTokenFromProvider()
    {
        HttpRequestMessage? seen = null;
        var inner = new FakeHttpMessageHandler(request => { seen = request; return new HttpResponseMessage(HttpStatusCode.OK); });
        var provider = new FakeAccessTokenProvider { Token = "abc-123" };
        using var client = new HttpClient(new AuthorizingHttpMessageHandler(provider) { InnerHandler = inner });

        await client.SendAsync(NewRequest());

        Assert.Equal("Bearer", seen!.Headers.Authorization!.Scheme);
        Assert.Equal("abc-123", seen.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_On401_RefreshesOnceAndRetriesWithNewToken()
    {
        var inner = new FakeHttpMessageHandler(request =>
        {
            var token = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(token == "token-v2" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
        });
        var provider = new FakeAccessTokenProvider();
        using var client = new HttpClient(new AuthorizingHttpMessageHandler(provider) { InnerHandler = inner });

        var response = await client.SendAsync(NewRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, provider.RefreshCalls);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal(0, provider.SessionExpiredCalls);
    }

    [Fact]
    public async Task SendAsync_ConcurrentUnauthorizedRequests_ShareASingleRefresh()
    {
        var inner = new FakeHttpMessageHandler(request =>
        {
            var token = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(token == "token-v2" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
        });
        var provider = new FakeAccessTokenProvider { RefreshGate = new TaskCompletionSource() };
        using var client = new HttpClient(new AuthorizingHttpMessageHandler(provider) { InnerHandler = inner });

        var first = client.SendAsync(NewRequest());
        var second = client.SendAsync(NewRequest());
        await Task.Delay(50); // let both requests hit the 401 path and enter the refresh gate
        provider.RefreshGate.SetResult();

        var responses = await Task.WhenAll(first, second);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, provider.RefreshCalls);
    }

    [Fact]
    public async Task SendAsync_WhenRefreshFails_NotifiesSessionExpiredAndReturnsOriginal401WithoutRetrying()
    {
        var inner = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var provider = new FakeAccessTokenProvider { RefreshSucceeds = false };
        using var client = new HttpClient(new AuthorizingHttpMessageHandler(provider) { InnerHandler = inner });

        var response = await client.SendAsync(NewRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, provider.RefreshCalls);
        Assert.Equal(1, provider.SessionExpiredCalls);
        Assert.Single(inner.Requests); // no second network call once refresh has already failed
    }

    [Fact]
    public async Task SendAsync_WhenRetriedRequestIsStillUnauthorized_DoesNotLoop()
    {
        var inner = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var provider = new FakeAccessTokenProvider(); // refresh "succeeds" but server keeps rejecting
        using var client = new HttpClient(new AuthorizingHttpMessageHandler(provider) { InnerHandler = inner });

        var response = await client.SendAsync(NewRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, provider.RefreshCalls);
        Assert.Equal(2, inner.Requests.Count); // exactly one retry, never more
    }

    [Fact]
    public async Task SendAsync_WhenNotUnauthorized_NeverCallsRefresh()
    {
        var inner = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var provider = new FakeAccessTokenProvider();
        using var client = new HttpClient(new AuthorizingHttpMessageHandler(provider) { InnerHandler = inner });

        await client.SendAsync(NewRequest());

        Assert.Equal(0, provider.RefreshCalls);
    }
}
