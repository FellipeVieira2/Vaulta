using Vaulta.App.Core.Catalog;
using Vaulta.Catalog.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class ScannerPendingTests
{
    private static ScannerSessionCard Pending() => new(Guid.NewGuid(), Guid.Empty, null, "Sliggoo", "", "", "normal", "UNKNOWN", null, null,
        DateTimeOffset.UtcNow, VisualIdentification: new("Sliggoo", null, "pt", null, .8));

    [Fact]
    public void BelowThresholdResearchCanBeConfirmedWithoutChangingEvidence()
    {
        var visual = Pending().VisualIdentification! with { Confidence = .79, Finish = "normal" };
        var estimate = new ScannerMarketEstimateDto(42, "Web", DateTimeOffset.UtcNow, .79, visual,
            [new("https://example.com/card", "Card", 42, "BRL", "sale")]);
        Assert.Null(ScannerValuation.ResearchValue(estimate, visual));
        Assert.NotNull(ScannerValuation.ResearchValue(estimate, visual, confirmed: true));
        var session = ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow).Add(Pending() with { VisualIdentification = visual });
        Assert.Equal(.79, session.Cards[0].VisualIdentification!.Confidence);
        Assert.False(ScannerValuation.CanAutoAcceptVisual(visual));
    }

    [Fact]
    public void GradedResearchIsCompatibleOnlyWithSameCompanyGradeAndRetainsCapturedSerial()
    {
        var visual = Pending().VisualIdentification! with { Finish = "normal", Certification = new("PSA", "10", "MY-CERT") };
        var identity = visual with { Certification = new("PSA", "10", "OTHER-CERT") };
        var estimate = new ScannerMarketEstimateDto(42, "Web", DateTimeOffset.UtcNow, .8, identity,
            [new("https://example.com/card", "Card", 42, "BRL", "sale")]);
        var value = ScannerValuation.ResearchValue(estimate, visual);
        Assert.NotNull(value);
        Assert.Null(ScannerValuation.ResearchValue(estimate, visual with { Certification = null }));
        Assert.Null(ScannerValuation.ResearchValue(estimate, visual with { Certification = new("PSA", "9", "MY-CERT") }));
        var session = ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow).Add(Pending() with { VisualIdentification = visual, MarketValue = value });
        Assert.Equal("MY-CERT", session.Cards[0].VisualIdentification!.Certification!.Number);
    }

    [Fact]
    public void PendingCardsCannotAcquireListingStateEvenIfRestoredWithInventoryIds()
    {
        var pending = Pending() with { ImportedItemId = Guid.NewGuid(), ListingDraftId = Guid.NewGuid() };
        var session = ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow) with { Cards = [pending] };
        Assert.Throws<InvalidOperationException>(() => session.RecordListingDraft(pending.ScanId, pending.ListingDraftId!.Value));
        Assert.Throws<InvalidOperationException>(() => session.BeginListingPublication(pending.ScanId, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => session.RecordListingPublished(pending.ScanId, pending.ListingDraftId!.Value));
    }

    [Theory]
    [InlineData(.8, true)]
    [InlineData(.7999, false)]
    [InlineData(double.NaN, false)]
    [InlineData(1.01, false)]
    public void AutomaticVisualAcceptanceUsesFiniteInclusiveThreshold(double confidence, bool accepted)
        => Assert.Equal(accepted, ScannerValuation.CanAutoAcceptVisual(Pending().VisualIdentification! with { Confidence = confidence }));

    [Fact]
    public async Task ResearchSourcesAndCheckDateSurviveAppRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vaulta-research-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileScannerSessionStore(directory);
            var now = DateTimeOffset.UtcNow;
            var value = new SessionMarketValue(42, "Web", now, Sources: [new("https://example.com/card", "Card", 42, "BRL", "sale")], IsEstimate: true);
            var session = ScannerSession.Start(Guid.NewGuid(), now).Add(Pending() with { MarketValue = value });
            await store.Save(session);
            var restored = await new FileScannerSessionStore(directory).Latest(session.OwnerId);
            Assert.Equal(42m, restored!.EstimatedValueBrl);
            Assert.Equal(now, restored.Cards[0].MarketValue!.QuotedAt);
            Assert.Equal("https://example.com/card", Assert.Single(restored.Cards[0].MarketValue!.Sources!).Url);
            Assert.True(restored.Cards[0].MarketValue!.IsEstimate);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ResearchEstimateSumsWithSourcesAndRejectsOtherFinish()
    {
        var visual = Pending().VisualIdentification! with { Finish = "normal" };
        var estimate = new ScannerMarketEstimateDto(42m, "Web", DateTimeOffset.UtcNow, .8, visual,
            [new("https://example.com/card", "Card", 42m, "BRL", "sale")]);
        var value = ScannerValuation.ResearchValue(estimate, visual);
        Assert.NotNull(value);
        Assert.Equal(42m, ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow).Add(Pending() with { MarketValue = value }).EstimatedValueBrl);
        Assert.Single(value.Sources!);
        Assert.Null(ScannerValuation.ResearchValue(estimate, visual with { Finish = "reverse" }));
    }

    [Fact]
    public void TrustedReadingWithoutNumberCountsWithoutFabricatingPrintingOrPrice()
    {
        var session = ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow).Add(Pending());
        Assert.Equal(Guid.Empty, Assert.Single(session.Cards).PrintingId);
        Assert.Equal(1, session.UnpricedCards);
        Assert.Throws<InvalidOperationException>(() => session.BeginOccurrenceImport(session.Cards[0].ScanId));
        Assert.Throws<InvalidOperationException>(() => session.Complete().BeginImport());
    }

    [Fact]
    public void MixedImportCompletesAfterResolvedCopiesAndRetainsPending()
    {
        var resolved = Pending() with { PrintingId = Guid.NewGuid() };
        var session = ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow).Add(Pending()).Add(resolved).Complete().BeginImport();
        session = session.RecordImported(resolved.ScanId, Guid.NewGuid());
        Assert.Equal(ScannerSessionPhase.Imported, session.Phase);
        Assert.Equal(2, session.Cards.Count);
        Assert.Throws<InvalidOperationException>(() => session.RecordOccurrenceImported(session.Cards[0].ScanId, Guid.NewGuid()));
    }
}
