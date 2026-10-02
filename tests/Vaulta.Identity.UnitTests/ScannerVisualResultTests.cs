using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure.Recognition;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class ScannerVisualResultTests
{
    [Fact]
    public async Task ReadableCardMissingFromCatalogStillReturnsVisualReadingWithoutInventedId()
    {
        var extractor = new Extractor();
        var provider = new EvidenceCardRecognitionProvider(extractor, new(new EmptyCatalog()), null, "openai", null);
        var scanner = new ScannerService([provider], new LocalSearch(), new Resolver());
        var result = await scanner.IdentifyAsync(new(Convert.ToBase64String([1]), "pokemon"), default);
        Assert.Empty(result.Candidates);
        Assert.Equal("Golisopod", result.VisualIdentification!.Name);
        Assert.Equal("026/086", result.VisualIdentification.CollectorNumber);
        Assert.Equal("pt-BR", result.VisualIdentification.Language);
        Assert.Equal(140, result.VisualIdentification.Hp);
        Assert.Equal(1, extractor.Calls);
    }

    private sealed class Extractor : ICardEvidenceExtractor
    {
        public int Calls;
        public Task<CardEvidence?> ExtractAsync(byte[] imageData, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<CardEvidence?>(new(new("pokemon", .99), new("Golisopod", .98), new("026/086", .98),
                new(null, 0), new(null, 0), new("pt-BR", .99), new(null, 0), "test", "test", new("140", .98)));
        }
    }
    private sealed class EmptyCatalog : ICardRecognitionCatalog
    { public Task<IReadOnlyList<RecognitionCatalogCard>> FindCandidatesAsync(string name, string? number, string game, CancellationToken ct) => Task.FromResult<IReadOnlyList<RecognitionCatalogCard>>([]); }
    private sealed class LocalSearch : ICatalogSearch
    {
        public Task<CatalogSearchPage> Search(string query, string? game, int page, int size, CancellationToken ct) => throw new InvalidOperationException();
        public Task<CatalogPrintingDetails?> GetPrinting(Guid id, CancellationToken ct) => throw new InvalidOperationException();
    }
    private sealed class Resolver : IExternalIdResolver
    { public Task<IReadOnlyDictionary<string, Guid>> ResolvePrintingIdsAsync(IReadOnlyList<string> ids, CancellationToken ct) => throw new InvalidOperationException("No invented ID may be resolved"); }
}
