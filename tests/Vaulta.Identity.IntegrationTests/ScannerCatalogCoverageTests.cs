using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class ScannerCatalogCoverageTests(ApiFixture fixture)
{
    [Fact]
    public async Task ImportingPortuguesePreservesEnglishPrintingIdsAndAvailability()
    {
        var provider = new Provider();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var sync = new CatalogSyncService(db, [provider], scope.ServiceProvider.GetRequiredService<IClock>(), NullLogger<CatalogSyncService>.Instance);
        await sync.Synchronize(provider.Code, provider.SetId, default);
        var englishId = await db.ExternalIds.Where(x => x.EntityType == "printing" && x.ExternalId == provider.CardId("79")).Select(x => x.EntityId).SingleAsync();
        provider.Language = "pt";
        await sync.Synchronize(provider.Code, provider.SetId, default);
        var portugueseId = await db.ExternalIds.Where(x => x.EntityType == "printing" && x.ExternalId == "pt:" + provider.CardId("79")).Select(x => x.EntityId).SingleAsync();
        Assert.NotEqual(englishId, portugueseId);
        Assert.Equal("en", (await db.Printings.AsNoTracking().SingleAsync(x => x.Id == englishId)).Language);
        Assert.Equal("pt", (await db.Printings.AsNoTracking().SingleAsync(x => x.Id == portugueseId)).Language);
        Assert.True((await db.Printings.AsNoTracking().SingleAsync(x => x.Id == englishId)).IsActive);
        provider.IncludeLast = false;
        await sync.Synchronize(provider.Code, provider.SetId, default);
        Assert.True((await db.Printings.AsNoTracking().SingleAsync(x => x.Id == englishId)).IsActive);
        Assert.False((await db.Printings.AsNoTracking().SingleAsync(x => x.Id == portugueseId)).IsActive);
    }

    [Fact]
    public async Task CollectorNumberIsFilteredBeforeFiftyCandidateLimit()
    {
        var provider = new Provider();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await new CatalogSyncService(db, [provider], scope.ServiceProvider.GetRequiredService<IClock>(), NullLogger<CatalogSyncService>.Instance)
            .Synchronize(provider.Code, provider.SetId, default);
        var matches = await new CatalogQueries(db).FindCandidatesAsync("Charmeleon", "079", "pokemon", default);
        var fromSet = Assert.Single(matches, x => x.ProviderSetId == provider.SetId);
        Assert.Equal("79", fromSet.Card.CollectorNumber);
    }

    private sealed class Provider : ICatalogProvider
    {
        public string Code => "tcgdex";
        public string Language { get; set; } = "en";
        public string SetId { get; } = "scanner-" + Guid.NewGuid().ToString("N");
        public string CardId(string number) => SetId + "-" + number;
        public bool IncludeLast { get; set; } = true;
        private ProviderSet Set => new(SetId, "Scanner test " + SetId, null, null);
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken ct) => Task.FromResult<IReadOnlyList<ProviderSet>>([Set]);
        public Task<ProviderSetDetails> GetSetDetails(string id, CancellationToken ct) => Task.FromResult(new ProviderSetDetails(Set,
            Enumerable.Range(1, IncludeLast ? 79 : 78).Select(n => new ProviderPrinting(CardId(n.ToString()), "Charmeleon", n.ToString(), Language,
                "Rare", null, [new("holo", "Holo", "holo")])).ToArray()));
    }
}
