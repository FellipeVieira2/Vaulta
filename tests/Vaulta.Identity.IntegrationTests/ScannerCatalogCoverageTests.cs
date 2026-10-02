using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Catalog.Infrastructure.Recognition;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class ScannerCatalogCoverageTests(ApiFixture fixture)
{
    [Fact]
    public async Task PartialScannerDiscoveryPreservesOtherPrintingsAndReusesCanonicalIds()
    {
        var provider = new Provider();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var sync = new CatalogSyncService(db, [provider], scope.ServiceProvider.GetRequiredService<IClock>(), NullLogger<CatalogSyncService>.Instance);
        await sync.Synchronize(provider.Code, provider.SetId, default);
        var englishIds = await db.Printings.AsNoTracking().Where(p => db.ExternalIds.Any(x => x.EntityType == "printing" && x.EntityId == p.Id
            && x.ExternalId.StartsWith(provider.SetId))).Select(x => x.Id).ToArrayAsync();
        var details = new ProviderSetDetails(new(provider.SetId, "Coleção traduzida " + provider.SetId, null, null),
            [new(provider.CardId("79"), "Charmeleon", "079/102", "pt", "Rare", null, [new("holo", "Holo", "holo")])]);
        Assert.True(await sync.ImportAsync([details], default));
        var first = await db.ExternalIds.AsNoTracking().SingleAsync(x => x.EntityType == "printing" && x.ExternalId == "pt:" + provider.CardId("79"));
        var setId = await db.Printings.Where(x => x.Id == first.EntityId).Select(x => x.SetId).SingleAsync();
        Assert.Equal("Scanner test " + provider.SetId, (await db.Sets.AsNoTracking().SingleAsync(x => x.Id == setId)).Name);
        Assert.True(await sync.ImportAsync([details], default));
        Assert.Equal(first.EntityId, (await db.ExternalIds.AsNoTracking().SingleAsync(x => x.EntityType == "printing" && x.ExternalId == first.ExternalId)).EntityId);
        Assert.Equal(79, englishIds.Length);
        Assert.Equal(79, await db.Printings.CountAsync(x => englishIds.Contains(x.Id) && x.IsActive));
        var evidence = new CardEvidence(new("pokemon", .99), new("Charmeleon", .99), new("079/102", .99),
            new(null, 0), new(details.Set.Name, .99), new("pt-BR", .99), new(null, 0), "test", "test");
        var candidate = Assert.Single((await new CardEvidenceCatalogMatcher(new CatalogQueries(db)).MatchAsync(evidence, "pokemon", default)).Candidates);
        Assert.Equal(first.EntityId.ToString(), candidate.PrintingId);
        Assert.True(candidate.HasCollectorNumberMatch);
        Assert.Empty(await db.DailyMarketSnapshots.Where(x => x.PrintingId == first.EntityId).ToListAsync());
    }

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
