using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Vaulta.Assets.Contracts;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Collection.Contracts;
using Vaulta.Identity.Contracts;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class AssetStorageFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task PresignedUploadConfirmAttachAndPrivateReadWorkWithRealMinio()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var minio = new ContainerBuilder("minio/minio:RELEASE.2025-09-07T16-13-09Z")
            .WithEnvironment("MINIO_ROOT_USER", "vaulta-test")
            .WithEnvironment("MINIO_ROOT_PASSWORD", secret)
            .WithCommand("server", "/data")
            .WithPortBinding(9000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                r.ForPort(9000).ForPath("/minio/health/live")))
            .Build();
        await minio.StartAsync();
        var endpoint = $"http://{minio.Hostname}:{minio.GetMappedPublicPort(9000)}";
        using var s3 = new AmazonS3Client(new BasicAWSCredentials("vaulta-test", secret),
            new AmazonS3Config { ServiceURL = endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await s3.PutBucketAsync(new PutBucketRequest { BucketName = "vaulta-assets-test" });
        await using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Assets:S3:ServiceUrl"] = endpoint, ["Assets:S3:AccessKey"] = "vaulta-test",
                ["Assets:S3:SecretKey"] = secret, ["Assets:S3:Bucket"] = "vaulta-assets-test",
                ["Assets:S3:ForcePathStyle"] = "true"
            })));
        using var client = factory.CreateClient();
        var registration = new RegisterRequest($"{Guid.NewGuid():N}@example.com", "Disposable-Password123!",
            "u" + Guid.NewGuid().ToString("N")[..20], "Asset test");
        (await client.PostAsJsonAsync("/api/v1/auth/register", registration)).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(registration.Email, registration.Password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken);
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        var create = await client.PostAsJsonAsync("/api/v1/assets/uploads",
            new CreateAssetUploadRequest("collection-item", "image/png", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
        create.EnsureSuccessStatusCode();
        var asset = (await create.Content.ReadFromJsonAsync<CreateAssetUploadResponse>())!;
        using var objectClient = new HttpClient();
        using var body = new ByteArrayContent(bytes);
        body.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        (await objectClient.PutAsync(asset.UploadUrl, body)).EnsureSuccessStatusCode();
        var confirm = await client.PostAsync($"/api/v1/assets/{asset.AssetId}/confirm", null);
        confirm.EnsureSuccessStatusCode();
        Assert.Equal("ready", (await confirm.Content.ReadFromJsonAsync<ConfirmAssetUploadResponse>())!.Status);

        Guid printingId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var provider = new AssetTestCatalogProvider();
            var sync = new CatalogSyncService(db, [provider], scope.ServiceProvider.GetRequiredService<IClock>(), NullLogger<CatalogSyncService>.Instance);
            await sync.Synchronize(provider.Code, provider.SetId, default);
            printingId = await db.Printings.Where(p => p.Set.Name == provider.SetId).Select(p => p.Id).SingleAsync();
        }
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var add = await client.PostAsJsonAsync("/api/v1/me/collection/items",
            new AddCollectibleItemsRequest(printingId, null, 1, "NEAR_MINT", null, null, null));
        add.EnsureSuccessStatusCode();
        var itemId = (await add.Content.ReadFromJsonAsync<AddCollectibleItemsResponse>())!.CreatedItems.Single().Id;
        (await client.PostAsJsonAsync($"/api/v1/me/collection/items/{itemId}/assets",
            new AttachCollectibleItemAssetRequest(asset.AssetId, "FRONT", 0))).EnsureSuccessStatusCode();
        var item = (await client.GetFromJsonAsync<CollectibleItemDto>($"/api/v1/me/collection/items/{itemId}"))!;
        var readUrl = Assert.Single(item.Assets).Url;
        Assert.Equal(bytes, await objectClient.GetByteArrayAsync(readUrl));
        Assert.Equal(HttpStatusCode.Forbidden, (await objectClient.GetAsync(new Uri(readUrl).GetLeftPart(UriPartial.Path))).StatusCode);
    }

    private sealed class AssetTestCatalogProvider : ICatalogProvider
    {
        public string Code => "asset-test";
        public string SetId { get; } = "asset-set-" + Guid.NewGuid().ToString("N");
        private ProviderSet Set => new(SetId, SetId, null, null);
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderSet>>([Set]);
        public Task<ProviderSetDetails> GetSetDetails(string setId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderSetDetails(Set, [new("asset-card-" + SetId, "Asset test card", "1", "en", "common", null, [])]));
    }
}
