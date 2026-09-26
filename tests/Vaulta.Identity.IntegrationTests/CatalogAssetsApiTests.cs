using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vaulta.Assets.Contracts;
using Vaulta.Identity.Application;
using Vaulta.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class CatalogAssetsApiTests(ApiFixture fixture)
{
    [Fact]
    public async Task CatalogSearchAndPrintingEndpointReturnEmptyResultsForUnknownCards()
    {
        using var client = fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/v1/catalog/search?q=unknown-card");
        response.EnsureSuccessStatusCode();
        Assert.Empty((await response.Content.ReadFromJsonAsync<IReadOnlyList<Vaulta.Catalog.Contracts.CatalogSearchResult>>())!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/catalog/search?q=%21%21%21")).StatusCode);
    }

    [Fact]
    public async Task CatalogSyncIsIdempotentAndSearchable()
    {
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var provider = new FakeCatalogProvider();
            var sync = new CatalogSyncService(db, [provider], scope.ServiceProvider.GetRequiredService<Vaulta.SharedKernel.IClock>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CatalogSyncService>.Instance);
            await sync.Synchronize("fake", "all", CancellationToken.None);
            await sync.Synchronize("fake", "all", CancellationToken.None);

            Assert.Equal(1, await db.Sets.CountAsync());
            Assert.Equal(1, await db.Cards.CountAsync());
            Assert.Equal(1, await db.Printings.CountAsync());
            Assert.Equal(3, await db.ExternalIds.CountAsync());
            Assert.Equal(2, await db.SyncRuns.CountAsync(x => x.Status == "completed"));
        }

        using var client = fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/v1/catalog/search?q=pikachu");
        response.EnsureSuccessStatusCode();
        var results = (await response.Content.ReadFromJsonAsync<IReadOnlyList<CatalogSearchResult>>())!;
        Assert.Single(results);
        Assert.Equal("Pikachu", results[0].CardName);
    }

    [Fact]
    public async Task AssetUploadRequiresAuthenticationAndRejectsUnsupportedType()
    {
        using var client = fixture.Factory.CreateClient();
        var unauthenticated = await client.PostAsJsonAsync("/api/v1/assets/uploads", new CreateAssetUploadRequest("collection-item", "image/jpeg", 100, null));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var request = new RegisterRequest($"{Guid.NewGuid():N}@example.com", "Secure-Test-Password1!", "u" + Guid.NewGuid().ToString("N")[..20], "Collector");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/auth/register", request)).StatusCode);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password));
        login.EnsureSuccessStatusCode();
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var rejected = await client.PostAsJsonAsync("/api/v1/assets/uploads", new CreateAssetUploadRequest("collection-item", "application/pdf", 100, null));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    private sealed class FakeCatalogProvider : ICatalogProvider
    {
        public string Code => "fake";
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderSet>>([new("set-1", "Test Set", "TST", new DateOnly(2026, 1, 1))]);
        public Task<IReadOnlyList<ProviderPrinting>> GetPrintings(string setId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderPrinting>>([new("card-1", "Pikachu", "007 / 100", "en", "Rare", null, ["Holofoil"])]);
    }

}
