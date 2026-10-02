using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class ScannerCatalogDiscoveryTests
{
    [Fact]
    public async Task PortugueseFullNumberImportsOnlyMatchingOfficialSetTotal()
    {
        var importer = new Importer();
        using var handler = new Handler(); using var client = Client(handler);
        var service = new TcgDexScannerCatalogEnricher(client, importer, NullLogger<TcgDexScannerCatalogEnricher>.Instance);
        Assert.True(await service.EnrichAsync(Evidence(), "pokemon", default));
        var set = Assert.Single(importer.Sets!);
        var printing = Assert.Single(set.Printings);
        Assert.Equal("me04", set.Set.ExternalId);
        Assert.Equal("Caos Ascendente", set.Set.Name);
        Assert.Null(set.Set.Code);
        Assert.Equal("me04-026", printing.ExternalId);
        Assert.Equal("026/86", printing.CollectorNumber);
        Assert.Equal("pt", printing.Language);
        Assert.Equal(["normal", "reverse"], printing.Variants.Select(x => x.Code));
        Assert.DoesNotContain(handler.Paths, x => x.Contains("pricing", StringComparison.Ordinal));
        Assert.Equal(3, handler.Paths.Count);
    }

    [Theory]
    [InlineData("pokemon", .7, "pt-BR", "026/086")]
    [InlineData("yugioh", .98, "pt-BR", "026/086")]
    [InlineData("pokemon", .98, null, "026/086")]
    [InlineData("pokemon", .98, "pt-BR", null)]
    public async Task WeakOrUnsupportedEvidenceDoesNotTriggerExternalLookup(string game, double confidence, string? language, string? number)
    {
        var importer = new Importer(); using var handler = new Handler(); using var client = Client(handler);
        var service = new TcgDexScannerCatalogEnricher(client, importer, NullLogger<TcgDexScannerCatalogEnricher>.Instance);
        var evidence = Evidence() with { Name = new("Golisopod", confidence), Language = new(language, language is null ? 0 : .99), CollectorNumber = new(number, number is null ? 0 : .98) };
        Assert.False(await service.EnrichAsync(evidence, game, default));
        Assert.Null(importer.Sets); Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task UnverifiedPrintedCodeDoesNotPreventImportingCatalogCandidates()
    {
        var importer = new Importer(); using var handler = new Handler(); using var client = Client(handler);
        var service = new TcgDexScannerCatalogEnricher(client, importer, NullLogger<TcgDexScannerCatalogEnricher>.Instance);
        Assert.True(await service.EnrichAsync(Evidence() with { SetCode = new("CR", .99), Hp = new("140", 1) }, "pokemon", default));
        Assert.Equal("me04-026", Assert.Single(Assert.Single(importer.Sets!).Printings).ExternalId);
        Assert.Null(Assert.Single(importer.Sets!).Set.Code);
    }

    [Theory]
    [InlineData("026/999")]
    [InlineData("999/086")]
    public async Task ConflictingNumberNeverCreatesCatalogIdentity(string number)
    {
        var importer = new Importer(); using var handler = new Handler(); using var client = Client(handler);
        var service = new TcgDexScannerCatalogEnricher(client, importer, NullLogger<TcgDexScannerCatalogEnricher>.Instance);
        Assert.False(await service.EnrichAsync(Evidence() with { CollectorNumber = new(number, .99) }, "pokemon", default));
        Assert.Null(importer.Sets);
    }

    [Fact]
    public async Task ProviderFailureReturnsMissAndDoesNotRetryOrImport()
    {
        var importer = new Importer(); using var handler = new Handler { Status = HttpStatusCode.ServiceUnavailable }; using var client = Client(handler);
        var service = new TcgDexScannerCatalogEnricher(client, importer, NullLogger<TcgDexScannerCatalogEnricher>.Instance);
        Assert.False(await service.EnrichAsync(Evidence(), "pokemon", default));
        Assert.Null(importer.Sets); Assert.Single(handler.Paths);
    }

    private static CardEvidence Evidence() => new(new("pokemon", .99), new("Golisopod", .98), new("026/086", .98),
        new(null, 0), new(null, 0), new("pt-BR", .99), new(null, 0), "test", "test");
    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new("https://api.tcgdex.net/v2/") };
    private sealed class Importer : ICatalogDiscoveryImporter
    {
        public IReadOnlyList<ProviderSetDetails>? Sets;
        public Task<bool> ImportAsync(IReadOnlyList<ProviderSetDetails> sets, CancellationToken ct) { Sets = sets; return Task.FromResult(true); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            Assert.StartsWith("/v2/pt/cards", request.RequestUri.AbsolutePath);
            var json = request.RequestUri.AbsolutePath.EndsWith("/cards", StringComparison.Ordinal)
                ? """[{"id":"me04-026","name":"Golisopod","localId":"026"},{"id":"swsh10.5-026","name":"Golisopod","localId":"026"}]"""
                : request.RequestUri.AbsolutePath.EndsWith("me04-026", StringComparison.Ordinal)
                    ? """{"id":"me04-026","name":"Golisopod","localId":"026","hp":140,"set":{"id":"me04","name":"Caos Ascendente","cardCount":{"official":86,"total":122}},"variants":{"normal":true,"reverse":true,"holo":false}}"""
                    : """{"id":"swsh10.5-026","name":"Golisopod","localId":"026","hp":130,"set":{"id":"swsh10.5","name":"Pokémon GO","cardCount":{"official":78,"total":88}},"variants":{"normal":true,"reverse":true}}""";
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(json) });
        }
    }
}
