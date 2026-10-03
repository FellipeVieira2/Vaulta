using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure.Recognition;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class ScannerVisualResultTests
{
    [Fact]
    public async Task UnreadableCollectorNumberDoesNotTurnGuessedDigitsIntoIdentity()
    {
        var evidence = (await new Extractor().ExtractAsync([1], default))! with { CollectorNumber = new("062/066", .4) };
        var provider = new EvidenceCardRecognitionProvider(new GivenExtractor(evidence), new(new EmptyCatalog()), null, "openai", null);
        var result = await new ScannerService([provider], new LocalSearch(), new Resolver()).IdentifyAsync(new(Convert.ToBase64String([1]), null), default);
        Assert.Equal("Golisopod", result.VisualIdentification!.Name);
        Assert.Null(result.VisualIdentification.CollectorNumber);
    }
    [Theory]
    [InlineData(.799, null)]
    [InlineData(.8, "reverse")]
    [InlineData(.84, "reverse")]
    public async Task ModelVariantAtEightyPercentIsPreservedInsteadOfRequestingManualFinish(double confidence, string? expected)
    {
        var evidence = (await new Extractor().ExtractAsync([1], default))! with
        {
            Variant = new("reverse", confidence), Finish = new("textured", .99)
        };
        var provider = new EvidenceCardRecognitionProvider(new GivenExtractor(evidence), new(new EmptyCatalog()), null, "openai", null);
        var result = await new ScannerService([provider], new LocalSearch(), new Resolver()).IdentifyAsync(new(Convert.ToBase64String([1]), null), default);
        Assert.Equal(expected, result.VisualIdentification!.Finish);
        Assert.Equal("textured", result.VisualIdentification.SurfaceTreatment);
    }

    [Theory]
    [InlineData(.799, null)]
    [InlineData(.8, "normal")]
    public async Task FinishBelowEightyPercentRequiresReview(double confidence, string? expected)
    {
        var evidence = (await new Extractor().ExtractAsync([1], default))! with { Finish = new("normal", confidence) };
        var provider = new EvidenceCardRecognitionProvider(new GivenExtractor(evidence), new(new EmptyCatalog()), null, "openai", null);
        var result = await new ScannerService([provider], new LocalSearch(), new Resolver()).IdentifyAsync(new(Convert.ToBase64String([1]), null), default);
        Assert.Equal(expected, result.VisualIdentification!.Finish);
    }

    [Fact]
    public async Task LabelAndVisibleAttributesAreReturnedWithoutClaimingCertificateVerification()
    {
        var source = new Extractor();
        var evidence = (await source.ExtractAsync([1], default))! with
        {
            IsGraded = new("true", .99), GradingCompany = new("PSA", .99), Grade = new("10", .98),
            CertificationNumber = new("01234567", .96), Year = new("2025", .95), Rarity = new("Rare", .95),
            Finish = new("reverse", .6)
        };
        var provider = new EvidenceCardRecognitionProvider(new GivenExtractor(evidence), new(new EmptyCatalog()), null, "openai", null);
        var result = await new ScannerService([provider], new LocalSearch(), new Resolver()).IdentifyAsync(new(Convert.ToBase64String([1]), null), default);
        var visual = Assert.IsType<CardVisualIdentificationDto>(result.VisualIdentification);
        Assert.Equal("PSA", visual.Certification!.Company);
        Assert.Equal("10", visual.Certification.Grade);
        Assert.Equal("01234567", visual.Certification.Number);
        Assert.False(visual.Certification.Verified);
        Assert.Equal(2025, visual.Attributes!.Year);
        Assert.Null(visual.Finish);
    }

    [Fact]
    public async Task AnotherGameIsReadInOneCallWithoutPokemonFallbackOrCatalogGuessing()
    {
        var evidence = (await new Extractor().ExtractAsync([1], default))! with { GameCode = new("onepiece", .99), CollectorNumber = new("OP01-001", .99) };
        var extractor = new GivenExtractor(evidence);
        var provider = new EvidenceCardRecognitionProvider(extractor, new(new EmptyCatalog()), new ForbiddenFallback(), "openai", "ocr");
        var result = await new ScannerService([provider], new LocalSearch(), new Resolver()).IdentifyAsync(new(Convert.ToBase64String([1]), null), default);
        Assert.Empty(result.Candidates);
        Assert.Equal("onepiece", result.VisualIdentification!.GameCode);
        Assert.Equal("OP01-001", result.VisualIdentification.CollectorNumber);
        Assert.Equal(1, extractor.Calls);
    }

    private sealed class GivenExtractor(CardEvidence evidence) : ICardEvidenceExtractor
    {
        public int Calls;
        public Task<CardEvidence?> ExtractAsync(byte[] imageData, CancellationToken ct) { Calls++; return Task.FromResult<CardEvidence?>(evidence); }
    }
    private sealed class ForbiddenFallback : ICardRecognitionProvider
    {
        public string GameCode => "pokemon";
        public Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] data, CancellationToken ct) => throw new InvalidOperationException("Do not reinterpret another game as Pokemon");
    }

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
