using System.Net;
using System.Net.Http.Json;
using Vaulta.App.Core.Collection;
using Vaulta.App.Core.Http;
using Vaulta.App.Core.UnitTests.TestSupport;
using Vaulta.Collection.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Collection;

public sealed class CollectionClientTests
{
    private static readonly Guid PrintingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ItemId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static HttpClient CreateClient(FakeHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.test/") };

    [Fact]
    public async Task AddItemsAsync_SendsIdempotencyKeyHeader()
    {
        HttpRequestMessage? seen = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new AddCollectibleItemsResponse(Guid.NewGuid(), [new CollectibleItemCreatedDto(ItemId, Guid.NewGuid())], 1))
            };
        });
        var client = new global::Vaulta.App.Core.Collection.CollectionClient(CreateClient(handler));
        var intent = new AddToCollectionIntent();

        await client.AddItemsAsync(new AddCollectibleItemsRequest(PrintingId, null, 1, "NEAR_MINT", null, null, null), intent.IdempotencyKey);

        Assert.Equal(intent.IdempotencyKey, seen!.Headers.GetValues("Idempotency-Key").Single());
    }

    [Fact]
    public async Task AddItemsAsync_RetryWithSameIntent_ReusesTheSameKey()
    {
        var keysSeen = new List<string>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            keysSeen.Add(request.Headers.GetValues("Idempotency-Key").Single());
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new AddCollectibleItemsResponse(Guid.NewGuid(), [new CollectibleItemCreatedDto(ItemId, Guid.NewGuid())], 1))
            };
        });
        var client = new global::Vaulta.App.Core.Collection.CollectionClient(CreateClient(handler));
        var intent = new AddToCollectionIntent();
        var request = new AddCollectibleItemsRequest(PrintingId, null, 1, "NEAR_MINT", null, null, null);

        await client.AddItemsAsync(request, intent.IdempotencyKey); // original attempt
        await client.AddItemsAsync(request, intent.IdempotencyKey); // simulated retry after a timeout

        Assert.Equal(2, keysSeen.Count);
        Assert.Equal(keysSeen[0], keysSeen[1]);
    }

    [Fact]
    public async Task AddItemsAsync_RequiresAnIdempotencyKey()
    {
        var client = new global::Vaulta.App.Core.Collection.CollectionClient(CreateClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created))));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.AddItemsAsync(new AddCollectibleItemsRequest(PrintingId, null, 1, "NEAR_MINT", null, null, null), string.Empty));
    }

    [Fact]
    public async Task GetCollectionAsync_BuildsFilteredQueryString()
    {
        Uri? capturedUri = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            capturedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new CollectionPageDto([], 1, 20, 0)) };
        });
        var client = new global::Vaulta.App.Core.Collection.CollectionClient(CreateClient(handler));

        await client.GetCollectionAsync(new CollectionQuery("pikachu", "pokemon", null, "NEAR_MINT", null, 1, 20, "name"));

        Assert.Contains("query=pikachu", capturedUri!.Query);
        Assert.Contains("game=pokemon", capturedUri.Query);
        Assert.Contains("condition=NEAR_MINT", capturedUri.Query);
        Assert.Contains("sort=name", capturedUri.Query);
    }

    [Fact]
    public async Task GetEntryAsync_ParsesEntryDetails()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CollectionEntryDetailsDto(
                Guid.NewGuid(), PrintingId, null, "pokemon", "Pikachu", "Base Set", "025/102", "en", "common", null, null,
                1, new Dictionary<string, int> { ["NEAR_MINT"] = 1 }, [], 1, 20, 0))
        });
        var client = new global::Vaulta.App.Core.Collection.CollectionClient(CreateClient(handler));

        var entry = await client.GetEntryAsync(Guid.NewGuid(), 1, 20);

        Assert.Equal("Pikachu", entry.CardName);
        Assert.Equal(1, entry.Quantity);
    }

    [Fact]
    public async Task UpdateItemAsync_SendsVersionAndReturnsNewVersion()
    {
        var newVersion = Guid.NewGuid();
        HttpRequestMessage? seen = null;
        var handler = new FakeHttpMessageHandler(async request =>
        {
            seen = request;
            var body = await request.Content!.ReadFromJsonAsync<UpdateCollectibleItemRequest>();
            Assert.NotNull(body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { version = newVersion }) };
        });
        var client = new global::Vaulta.App.Core.Collection.CollectionClient(CreateClient(handler));
        var originalVersion = Guid.NewGuid();

        var result = await client.UpdateItemAsync(ItemId, new UpdateCollectibleItemRequest("NEAR_MINT", null, null, "notes", originalVersion));

        Assert.Equal(newVersion, result);
        Assert.Equal(HttpMethod.Put, seen!.Method);
    }

    [Fact]
    public async Task UpdateItemAsync_OnConflict_ThrowsApiExceptionWithoutSilentlyOverwriting()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new { title = "Version mismatch." })
        });
        var client = new global::Vaulta.App.Core.Collection.CollectionClient(CreateClient(handler));

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            client.UpdateItemAsync(ItemId, new UpdateCollectibleItemRequest("NEAR_MINT", null, null, null, Guid.NewGuid())));

        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task RemoveItemAsync_SendsVersionAsQueryParameter()
    {
        Uri? capturedUri = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            capturedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = new global::Vaulta.App.Core.Collection.CollectionClient(CreateClient(handler));
        var version = Guid.NewGuid();

        await client.RemoveItemAsync(ItemId, version);

        Assert.Contains($"version={version}", capturedUri!.Query);
    }
}
