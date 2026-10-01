using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Assets.Domain;
using Vaulta.Assets.Infrastructure;
using Vaulta.Identity.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.Marketplace.Contracts;
using Vaulta.Marketplace.Infrastructure;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class CommerceApiTests(ApiFixture fixture)
{
    [Fact]
    public async Task LoggedInUserCannotDeclareAnOrderPaid()
    {
        using var client = fixture.Factory.CreateClient();
        var request = new RegisterRequest($"{Guid.NewGuid():N}@example.test", "Secure-Test-Password1!",
            "u" + Guid.NewGuid().ToString("N")[..20], "Buyer");
        (await client.PostAsJsonAsync("/api/v1/auth/register", request)).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password));
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        var response = await client.PostAsJsonAsync($"/api/v1/orders/{Guid.NewGuid()}/confirm-payment", new { paymentId = "pay_forged" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WebhookAuthenticatesBeforeParsingOrProcessingPayload()
    {
        using var client = fixture.Factory.CreateClient();
        var denied = await client.PostAsJsonAsync("/api/v1/webhooks/asaas", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        client.DefaultRequestHeaders.Add("asaas-access-token", fixture.WebhookToken);
        var authenticated = await client.PostAsJsonAsync("/api/v1/webhooks/asaas", new { });
        Assert.Equal(HttpStatusCode.BadRequest, authenticated.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/webhooks/asaas", new[] { "invalid-root" })).StatusCode);
        var unrelated = await client.PostAsJsonAsync("/api/v1/webhooks/asaas", new
        {
            id = "evt_unrelated", @event = "TRANSFER_DONE",
            transfer = new { id = "tr_unrelated", status = "DONE", value = 100m, externalReference = "other-business-reference" }
        });
        Assert.Equal(HttpStatusCode.OK, unrelated.StatusCode);
    }

    [Fact]
    public async Task ListingDetailsAndBrowseReturnOwnersSignedPhotoUrl()
    {
        var owner = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var listing = Listing.Create(owner, Guid.NewGuid(), Guid.NewGuid(), null, "NM", 100, null, now);
        listing.AddPhoto(assetId, "FRONT", 0, now);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var assets = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            assets.Assets.Add(new Asset
            {
                Id = assetId, OwnerId = owner, ObjectKey = $"collection-item/{owner:N}/{assetId:N}",
                Purpose = "collection-item", Visibility = "private", Status = "ready",
                ContentType = "image/jpeg", ContentLength = 100, CreatedAt = now, ConfirmedAt = now
            });
            await assets.SaveChangesAsync();
            var marketplace = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            marketplace.SellerProfiles.Add(SellerProfile.Enable(owner, null, null, null, null, null, now));
            marketplace.Listings.Add(listing);
            await marketplace.SaveChangesAsync();
        }
        using var client = fixture.Factory.CreateClient();
        var detail = await client.GetFromJsonAsync<ListingDto>($"/api/v1/marketplace/listings/{listing.Id}");
        var photo = Assert.Single(detail!.Photos);
        Assert.Contains($"collection-item/{owner:N}/{assetId:N}", Uri.UnescapeDataString(photo.Url));
        Assert.True(photo.UrlExpiresAt > now);
        var browse = await client.GetFromJsonAsync<ListingPageDto>($"/api/v1/marketplace/listings?sellerUserId={owner}&pageSize=10000");
        Assert.Equal(100, browse!.PageSize);
        Assert.Equal(photo.AssetId, Assert.Single(Assert.Single(browse.Items).Photos).AssetId);
        Assert.NotEmpty(browse.Items[0].Photos[0].Url);
    }
}
