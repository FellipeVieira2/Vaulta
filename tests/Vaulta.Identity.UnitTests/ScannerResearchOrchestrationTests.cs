using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class ScannerResearchOrchestrationTests
{
    private static readonly Guid PrintingId = Guid.Parse("21111111-1111-1111-1111-111111111111");
    private static readonly Guid VariantId = Guid.Parse("31111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-03T10:00:00Z");
    private static readonly CardVisualIdentificationDto Corrected = new("Sliggoo", "067/086", "pt-BR", "Set", .92, "pokemon", 80, "normal");

    [Fact]
    public async Task ResearchReturnsPendingPriceAndCorrectedIdentityWithoutInventedPrinting()
    {
        var vision = new Vision();
        var result = await Scanner(vision, new Research(new(Corrected, Estimate(Corrected)))).IdentifyAsync(Request(), default);
        Assert.Empty(result.Candidates);
        Assert.Equal("067/086", result.VisualIdentification!.CollectorNumber);
        Assert.Equal(25m, result.MarketEstimate!.AmountBrl);
        Assert.Equal("https://market.example/card", Assert.Single(result.MarketEstimate.Sources).Url);
        Assert.Equal(1, vision.Calls);
    }

    [Fact]
    public async Task IdentityOnlyCorrectionResolvesCanonicalPrintingAndUsesExistingExactQuote()
    {
        var vision = new Vision();
        var result = await Scanner(vision, new Research(new(Corrected, null)), new Details(), new Resolver())
            .IdentifyAsync(Request(), default);
        Assert.Equal(PrintingId, Assert.Single(result.Candidates).PrintingId);
        Assert.Equal("067/086", result.VisualIdentification!.CollectorNumber);
        Assert.Equal(20m, result.MarketEstimate!.AmountBrl);
        Assert.False(result.MarketEstimate.IsEstimate);
        Assert.Equal(Now, result.MarketEstimate.CheckedAt);
        Assert.Equal(1, vision.Calls);
    }

    [Fact]
    public async Task MatchingRawQuoteSkipsPaidResearch()
    {
        var result = await Scanner(new Vision(true), new ForbiddenResearch(), new Details()).IdentifyAsync(Request(), default);
        Assert.Equal(20m, result.MarketEstimate!.AmountBrl);
        Assert.False(result.MarketEstimate.IsEstimate);
    }

    [Fact]
    public async Task ApprovedFullCollectorNumberUsesCanonicalQuoteWithEquivalentLeadingZeros()
    {
        var result = await Scanner(new Vision(true), new ForbiddenResearch(), new Details("67/86")).IdentifyAsync(Request(), default);
        Assert.Equal(20m, result.MarketEstimate!.AmountBrl);
        Assert.False(result.MarketEstimate.IsEstimate);
    }

    [Fact]
    public async Task CertifiedReadingNeverUsesRawQuoteAndKeepsCurrentSerial()
    {
        var card = Corrected with { Certification = new("PSA", "10", "other-serial") };
        var result = await Scanner(new Vision(true, true), new Research(new(card, Estimate(card))), new Details()).IdentifyAsync(Request(), default);
        Assert.Equal(25m, result.MarketEstimate!.AmountBrl);
        Assert.Equal("current-serial", result.VisualIdentification!.Certification!.Number);
        Assert.Equal("current-serial", result.MarketEstimate.Identification.Certification!.Number);
    }

    [Fact]
    public async Task ProviderExceptionPreservesVisionAndReturnsPartialResult()
    {
        var result = await Scanner(new Vision(), new ThrowingResearch()).IdentifyAsync(Request(), default);
        Assert.Equal("Sliggoo", result.VisualIdentification!.Name);
        Assert.Empty(result.Candidates);
        Assert.Null(result.MarketEstimate);
        Assert.NotNull(result.MarketIssue);
    }

    [Fact]
    public async Task FailedCanonicalQuoteFallsThroughToWebResearch()
    {
        var result = await Scanner(new Vision(true), new Research(new(Corrected, Estimate(Corrected))), new UnavailableDetails()).IdentifyAsync(Request(), default);
        Assert.Equal(25m, result.MarketEstimate!.AmountBrl);
    }

    [Fact]
    public async Task FailedCanonicalRematchingPreservesCorrectedIdentityAndResearchedPrice()
    {
        var result = await Scanner(new Vision(), new Research(new(Corrected, Estimate(Corrected))), resolver: new UnavailableResolver()).IdentifyAsync(Request(), default);
        Assert.Equal("067/086", result.VisualIdentification!.CollectorNumber);
        Assert.Equal(25m, result.MarketEstimate!.AmountBrl);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Scanner(new Vision(), new ThrowingResearch()).IdentifyAsync(Request(), cancellation.Token));
    }

    private static ScannerService Scanner(Vision vision, IScannerMarketResearch research, IScannerCardDetailsReader? details = null, IScannerIdentityResolver? resolver = null)
        => new([vision], new CatalogSearch(), new ExternalResolver(), research, details, resolver);
    private static CardScanRequest Request() => new(Convert.ToBase64String([1]), "pokemon");
    private static ScannerMarketEstimateDto Estimate(CardVisualIdentificationDto card) => new(25m, "Web", Now, .92, card,
        [new("https://market.example/card", "Sliggoo", 25m, "BRL", "listing", Now)]);
    private sealed class Vision(bool matched = false, bool certified = false) : IVisualCardRecognitionProvider
    {
        public int Calls; public string GameCode => "pokemon";
        public Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] image, CancellationToken ct) => throw new InvalidOperationException();
        public Task<CardRecognitionReading> IdentifyWithEvidenceAsync(byte[] image, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Calls++;
            return Task.FromResult(new CardRecognitionReading(matched ? [Candidate()] : [],
                new(new("pokemon", .99), new("Sliggoo", .92), new(matched ? "067/086" : "062/066", .9), new(null, 0), new("Set", .9), new("pt-BR", .99), new("normal", .99), "test", "test",
                    IsGraded: certified ? new("true", .99) : null, GradingCompany: certified ? new("PSA", .99) : null,
                    Grade: certified ? new("10", .99) : null, CertificationNumber: certified ? new("current-serial", .99) : null)));
        }
    }
    private static CardRecognitionCandidate Candidate() => new(PrintingId.ToString(), "Sliggoo", "Set", "067/086", null, null, null, null, ["normal"], .92, true);
    private sealed class Research(ScannerMarketResearchResultDto result) : IScannerMarketResearch
    { public Task<ScannerMarketResearchResultDto?> ResearchAsync(CardVisualIdentificationDto card, byte[]? image, CancellationToken ct) => Task.FromResult<ScannerMarketResearchResultDto?>(result); }
    private sealed class ForbiddenResearch : IScannerMarketResearch
    { public Task<ScannerMarketResearchResultDto?> ResearchAsync(CardVisualIdentificationDto card, byte[]? image, CancellationToken ct) => throw new InvalidOperationException("Paid research must be skipped"); }
    private sealed class ThrowingResearch : IScannerMarketResearch
    { public Task<ScannerMarketResearchResultDto?> ResearchAsync(CardVisualIdentificationDto card, byte[]? image, CancellationToken ct) => throw new HttpRequestException("Unavailable"); }
    private sealed class Resolver : IScannerIdentityResolver
    { public Task<IReadOnlyList<CardRecognitionCandidate>> ResolveAsync(CardVisualIdentificationDto card, CancellationToken ct) => Task.FromResult<IReadOnlyList<CardRecognitionCandidate>>(card.CollectorNumber == "067/086" ? [Candidate()] : []); }
    private sealed class UnavailableResolver : IScannerIdentityResolver
    { public Task<IReadOnlyList<CardRecognitionCandidate>> ResolveAsync(CardVisualIdentificationDto card, CancellationToken ct) => throw new HttpRequestException("Unavailable"); }
    private sealed class UnavailableDetails : IScannerCardDetailsReader
    { public Task<ScannerCardDetailsDto?> GetAsync(Guid id, CancellationToken ct) => throw new HttpRequestException("Unavailable"); }
    private sealed class ExternalResolver : IExternalIdResolver
    { public Task<IReadOnlyDictionary<string, Guid>> ResolvePrintingIdsAsync(IReadOnlyList<string> ids, CancellationToken ct) => throw new InvalidOperationException("No IDs invented"); }
    private sealed class CatalogSearch : ICatalogSearch
    {
        public Task<CatalogSearchPage> Search(string query, string? game, int page, int size, CancellationToken ct) => throw new InvalidOperationException();
        public Task<CatalogPrintingDetails?> GetPrinting(Guid id, CancellationToken ct) => throw new InvalidOperationException();
    }
    private sealed class Details(string number = "067/086") : IScannerCardDetailsReader
    {
        public Task<ScannerCardDetailsDto?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<ScannerCardDetailsDto?>(new(
            new(PrintingId, Guid.Empty, Guid.Empty, "pokemon", "Set", "Sliggoo", number, "pt-BR", null, null, [new(VariantId, "normal", "Normal")]),
            new Dictionary<string, string>(), [new(VariantId, "Normal", 20m, 20m, "BRL", "Cardmarket", Now, 1m, Now, [])], null, Now, Now.AddHours(22)));
    }
}

