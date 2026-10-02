using System.Net;
using System.Net.Http.Json;
using Vaulta.App.Core.Marketplace;
using Vaulta.Marketplace.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Marketplace;

public sealed class ListingDraftClientTests
{
    [Fact]
    public async Task CreateCarriesStableClientKeyAndNullManualPrice()
    {
        var handler = new Requests(); var client = Client(handler);
        var request = new CreateListingDraftRequest("scanner-session-scan", Guid.NewGuid(), Guid.NewGuid(), null, "UNKNOWN", null, null);
        await client.CreateAsync(request);
        Assert.Equal("POST api/v1/me/seller/listing-drafts", handler.Route);
        Assert.Equal(request, System.Text.Json.JsonSerializer.Deserialize<CreateListingDraftRequest>(handler.Payload!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task PublishCarriesDurableKeyAndOriginalVersion()
    {
        var handler = new Requests(); var client = Client(handler); var id = Guid.NewGuid();
        var request = new PublishListingDraftRequest("publish-stable-key", Guid.NewGuid());
        await client.PublishAsync(id, request);
        Assert.Equal($"POST api/v1/me/seller/listing-drafts/{id}/publish", handler.Route);
        Assert.Contains("publish-stable-key", handler.Payload);
        Assert.Contains(request.Version.ToString(), handler.Payload);
    }

    [Fact]
    public async Task PhotoMutationCarriesVersionAndReturnsCurrentDraft()
    {
        var handler = new Requests(); var client = Client(handler); var id = Guid.NewGuid();
        var result = await client.AddPhotoAsync(id, new(Guid.NewGuid(), "FRONT", 0, Guid.NewGuid()));
        Assert.Equal($"POST api/v1/me/seller/listing-drafts/{id}/photos", handler.Route); Assert.Equal(handler.Draft.Id, result.Id);
    }

    [Fact]
    public async Task GetAndRemoveUsePrivateRouteAndVersion()
    {
        var handler = new Requests(); var client = Client(handler); var id = Guid.NewGuid(); var asset = Guid.NewGuid(); var version = Guid.NewGuid();
        await client.GetAsync(id); Assert.Equal($"GET api/v1/me/seller/listing-drafts/{id}", handler.Route);
        await client.RemovePhotoAsync(id, asset, version);
        Assert.Equal($"DELETE api/v1/me/seller/listing-drafts/{id}/photos/{asset}?version={version}", handler.Route);
    }

    [Fact]
    public async Task CancellationPropagatesToRequest()
    {
        var handler = new Requests(); using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).GetAsync(Guid.NewGuid(), ct.Token));
    }

    private static ListingDraftClient Client(Requests handler) => new(new HttpClient(handler) { BaseAddress = new("https://example.test/") });
    private sealed class Requests : HttpMessageHandler
    {
        public string? Route { get; private set; }
        public string? Payload { get; private set; }
        public ListingDraftDto Draft { get; } = new(Guid.NewGuid(), "key", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "UNKNOWN", null, "BRL", "draft", null, [], DateTimeOffset.UtcNow, Guid.NewGuid());
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Route = $"{request.Method} {request.RequestUri!.PathAndQuery.TrimStart('/')}";
            Payload = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(Draft) };
        }
    }
}
