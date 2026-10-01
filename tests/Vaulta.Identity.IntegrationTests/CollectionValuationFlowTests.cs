using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Collection.Contracts;
using Vaulta.Collection.Domain;
using Vaulta.Collection.Infrastructure;
using Vaulta.Identity.Contracts;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class CollectionValuationFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task ValuationUsesAllOwnedActiveUnitsIncludingListedCopiesAndProtectsEntryOwnership()
    {
        using var registration = fixture.Factory.CreateClient();
        var request = new RegisterRequest($"{Guid.NewGuid():N}@example.test", "Secure-Test-Password1!", "u" + Guid.NewGuid().ToString("N")[..20], "Collector");
        (await registration.PostAsJsonAsync("/api/v1/auth/register", request)).EnsureSuccessStatusCode();
        var auth = (await registration.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password)));
        auth.EnsureSuccessStatusCode(); var account = (await auth.Content.ReadFromJsonAsync<AuthResponse>())!;
        var now = DateTimeOffset.UtcNow; var printing = Guid.NewGuid(); var variant = Guid.NewGuid();
        var entry = CollectionEntry.Create(account.User.Id, printing, variant, now);
        var items = entry.AddItems(55, "NM", null, null, null, now).ToArray();
        items[0].Remove(items[0].Version, now);
        var soldListing = Guid.NewGuid(); items[1].ReserveForListing(soldListing, now); items[1].RecordSale(soldListing, Guid.NewGuid(), Guid.NewGuid(), now);
        items[2].ReserveForListing(Guid.NewGuid(), now); // remains owned until delivery
        var unpriced = CollectionEntry.Create(account.User.Id, printing, null, now);
        var unpricedItems = unpriced.AddItems(2, "NM", null, null, null, now);
        var foreign = CollectionEntry.Create(Guid.NewGuid(), printing, variant, now);
        var foreignItems = foreign.AddItems(1, "NM", null, null, null, now);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
            db.Entries.AddRange(entry, unpriced, foreign); db.Items.AddRange(items.Concat(unpricedItems).Concat(foreignItems)); await db.SaveChangesAsync();
        }
        var pricing = new Pricing(printing, variant, now);
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IScannerCardDetailsReader>(); services.AddSingleton<IScannerCardDetailsReader>(pricing); }));
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/collection/valuation")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", account.AccessToken);
        var value = (await client.GetFromJsonAsync<CollectionValuationDto>("/api/v1/me/collection/valuation"))!;
        Assert.Equal("BRL", value.Currency); Assert.Equal(1060m, value.EstimatedValueBrl);
        Assert.Equal(55, value.TotalItems); Assert.Equal(53, value.PricedItems); Assert.Equal(2, value.UnpricedItems);
        Assert.Equal(2, value.TotalEntries); Assert.True(value.IsPartial); Assert.Equal(1, pricing.Calls);
        Assert.Equal(53, Assert.Single(value.Comparisons).ComparedItems);
        Assert.Equal(265m, value.Comparisons[0].DifferenceBrl);
        value = (await client.GetFromJsonAsync<CollectionValuationDto>($"/api/v1/me/collection/valuation?entryId={entry.Id}"))!;
        Assert.Equal(1060m, value.EstimatedValueBrl); Assert.Equal(53, value.TotalItems); Assert.False(value.IsPartial);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/me/collection/valuation?entryId={foreign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/me/collection/valuation?entryId=" + Guid.Empty)).StatusCode);
    }

    private sealed class Pricing(Guid printing, Guid variant, DateTimeOffset now) : IScannerCardDetailsReader
    {
        public int Calls { get; private set; }
        public Task<ScannerCardDetailsDto?> GetAsync(Guid printingId, CancellationToken ct)
        {
            Calls++; Assert.Equal(printing, printingId);
            return Task.FromResult<ScannerCardDetailsDto?>(new(
                new(printing, Guid.NewGuid(), Guid.NewGuid(), "pokemon", "Test", "Test card", "1", "en", null, null, [new(variant, "normal", "Normal")]),
                new Dictionary<string, string>(), [new(variant, "Normal", 20, 4, "USD", "TCGplayer", now, 5, now, [new(7, 15, 5, 33.33m)])], null));
        }
    }
}
