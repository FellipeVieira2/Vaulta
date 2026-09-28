using System.Net;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Http;
using Vaulta.App.Core.UnitTests.TestSupport;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class CatalogClientTests
{
    private static HttpClient CreateClient(FakeHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.test/") };

    [Fact]
    public async Task SearchAsync_BuildsQueryStringAndParsesPage()
    {
        Uri? capturedUri = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            capturedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(
                    """
                    {"items":[{"printingId":"11111111-1111-1111-1111-111111111111","gameCode":"pokemon","setId":"22222222-2222-2222-2222-222222222222","setName":"Base Set","cardName":"Pikachu","collectorNumber":"025/102","language":"en","rarity":"common","artworkUrl":"https://x/y.png"}],"page":2,"pageSize":10,"totalCount":1}
                    """)
            };
        });
        var client = new CatalogClient(CreateClient(handler));

        var page = await client.SearchAsync("pikachu", "pokemon", 2, 10, CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(10, page.PageSize);
        var item = Assert.Single(page.Items);
        Assert.Equal("Pikachu", item.CardName);
        Assert.Contains("q=pikachu", capturedUri!.Query);
        Assert.Contains("page=2", capturedUri.Query);
        Assert.Contains("pageSize=10", capturedUri.Query);
        Assert.Contains("game=pokemon", capturedUri.Query);
    }

    [Fact]
    public async Task SearchAsync_OmitsGameWhenNotProvided()
    {
        Uri? capturedUri = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            capturedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent("""{"items":[],"page":1,"pageSize":20,"totalCount":0}""") };
        });
        var client = new CatalogClient(CreateClient(handler));

        await client.SearchAsync("charizard", null, 1, 20, CancellationToken.None);

        Assert.DoesNotContain("game=", capturedUri!.Query);
    }

    [Fact]
    public async Task GetPrintingAsync_ParsesVariants()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(
                """
                {"printingId":"11111111-1111-1111-1111-111111111111","cardId":"33333333-3333-3333-3333-333333333333","setId":"22222222-2222-2222-2222-222222222222","gameCode":"pokemon","setName":"Base Set","cardName":"Pikachu","collectorNumber":"025/102","language":"en","rarity":"common","artworkUrl":null,"variants":[{"id":"44444444-4444-4444-4444-444444444444","code":"normal","name":"Normal"}]}
                """)
        });
        var client = new CatalogClient(CreateClient(handler));

        var details = await client.GetPrintingAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), CancellationToken.None);

        var variant = Assert.Single(details.Variants);
        Assert.Equal("normal", variant.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 404)]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    [InlineData(HttpStatusCode.InternalServerError, 500)]
    public async Task GetPrintingAsync_TranslatesHttpErrorsToApiException(HttpStatusCode statusCode, int expectedStatus)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = JsonContent("""{"title":"Some server detail","status":400}""")
        });
        var client = new CatalogClient(CreateClient(handler));

        var error = await Assert.ThrowsAsync<ApiException>(() => client.GetPrintingAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.DoesNotContain("Some server detail", error.Message);
    }

    private static StringContent JsonContent(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}
