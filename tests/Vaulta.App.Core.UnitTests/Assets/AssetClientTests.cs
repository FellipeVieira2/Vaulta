using System.Net;
using System.Net.Http.Json;
using Vaulta.App.Core.Assets;
using Vaulta.App.Core.Http;
using Vaulta.App.Core.UnitTests.TestSupport;
using Vaulta.Assets.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Assets;

public sealed class AssetClientTests
{
    private static HttpClient CreateClient(FakeHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.test/") };

    [Fact]
    public async Task CreateUploadAsync_PostsRequestAndParsesResponse()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var apiHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CreateAssetUploadResponse(Guid.NewGuid(), "https://s3.test/bucket/key?sig=abc", expiresAt, "collection-item/key.jpg"))
        });
        var client = new AssetClient(CreateClient(apiHandler), new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));

        var response = await client.CreateUploadAsync(new CreateAssetUploadRequest("collection-item", "image/jpeg", 1024, null));

        Assert.Equal("collection-item/key.jpg", response.ObjectKey);
        Assert.Equal(expiresAt, response.ExpiresAt);
    }

    [Fact]
    public async Task ConfirmUploadAsync_PostsToConfirmEndpoint()
    {
        Uri? capturedUri = null;
        var assetId = Guid.NewGuid();
        var apiHandler = new FakeHttpMessageHandler(request =>
        {
            capturedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ConfirmAssetUploadResponse(assetId, "confirmed", null)) };
        });
        var client = new AssetClient(CreateClient(apiHandler), new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));

        var response = await client.ConfirmUploadAsync(assetId);

        Assert.Equal("confirmed", response.Status);
        Assert.Contains($"assets/{assetId}/confirm", capturedUri!.ToString());
    }

    [Fact]
    public async Task UploadToPresignedUrlAsync_DoesNotLeakApiAuthorizationHeaderOntoPresignedRequest()
    {
        HttpRequestMessage? seen = null;
        var presignedHandler = new FakeHttpMessageHandler(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var apiClient = CreateClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        apiClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "should-not-leak");
        var client = new AssetClient(apiClient, new HttpClient(presignedHandler));

        using var content = new MemoryStream([1, 2, 3]);
        await client.UploadToPresignedUrlAsync("https://s3.test/bucket/key?sig=abc", content, "image/jpeg", CancellationToken.None);

        Assert.Null(seen!.Headers.Authorization);
        Assert.Equal("image/jpeg", seen.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(HttpMethod.Put, seen.Method);
    }

    [Fact]
    public async Task UploadToPresignedUrlAsync_OnFailure_ThrowsApiException()
    {
        var presignedHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = new AssetClient(CreateClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))), new HttpClient(presignedHandler));

        using var content = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<ApiException>(() =>
            client.UploadToPresignedUrlAsync("https://s3.test/bucket/key?sig=abc", content, "image/jpeg", CancellationToken.None));
    }
}
