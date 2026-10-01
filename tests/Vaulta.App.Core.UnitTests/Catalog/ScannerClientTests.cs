using System.Net;
using System.Text;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Http;
using Vaulta.App.Core.UnitTests.TestSupport;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class ScannerClientTests
{
    [Fact]
    public async Task Scan_RenewsSessionAndPreservesMultipartImageOnRetry()
    {
        var bodies = new List<byte[]>();
        var provider = new FakeAccessTokenProvider();
        var inner = new FakeHttpMessageHandler(async request =>
        {
            Assert.Equal("?gameCode=pokemon", request.RequestUri!.Query);
            Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
            bodies.Add(await request.Content.ReadAsByteArrayAsync());
            return new HttpResponseMessage(request.Headers.Authorization?.Parameter == "token-v2" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"candidates\":[]}", Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(new AuthorizingHttpMessageHandler(provider) { InnerHandler = inner })
        { BaseAddress = new Uri("https://api.test/") };
        var result = await new ScannerClient(http).ScanCardAsync([0xff, 0xd8, 0xff, 0x45], "POKEMON");
        Assert.Empty(result.Candidates);
        Assert.Equal(1, provider.RefreshCalls);
        Assert.Equal(2, bodies.Count);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Contains("image/jpeg", Encoding.UTF8.GetString(bodies[0]));
    }

    [Fact]
    public async Task Scan_RejectsUnsupportedImageBeforeCallingApi()
    {
        var handler = new FakeHttpMessageHandler((Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new InvalidOperationException("Must not call API")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        await Assert.ThrowsAsync<ArgumentException>(() => new ScannerClient(http).ScanCardAsync([1, 2, 3], "pokemon"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Search_EncodesNameAndTranslatesApiFailure()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            Assert.Contains("query=Pikachu%20%26%20Raichu", request.RequestUri!.Query);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent("{\"title\":\"OCR unavailable\"}", Encoding.UTF8, "application/problem+json") };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var error = await Assert.ThrowsAsync<ApiException>(() => new ScannerClient(http).SearchCardsAsync("Pikachu & Raichu", "pokemon"));
        Assert.Equal(503, error.StatusCode);
    }
}
