using Vaulta.App.Core.Catalog;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class ScannerSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static ScannerSession NewSession() => ScannerSession.Start(Guid.NewGuid(), Now);
    private static ScannerSessionCard Card(Guid? printing = null, decimal? value = 20) => new(Guid.NewGuid(), printing ?? Guid.NewGuid(), null,
        "Card", "Set", "1", "Normal", "UNKNOWN", null, value.HasValue ? new(value.Value, "TCGplayer · PTAX", Now) : null, Now);

    [Fact]
    public void FivePacksAtTwentyReaisAndTotalHundredProduceSameCostAndEstimatedDifference()
    {
        var perPack = NewSession().ConfigureCost(5, PackCostMode.PerPack, 20).Add(Card(value: 125));
        var total = NewSession().ConfigureCost(5, PackCostMode.Total, 100).Add(Card(value: 125));
        Assert.Equal(100m, perPack.CostBrl); Assert.Equal(total.CostBrl, perPack.CostBrl);
        Assert.Equal(25m, perPack.EstimatedDifferenceBrl); Assert.Equal(25m, perPack.EstimatedDifferencePercent); Assert.Equal("BRL", perPack.Currency);
    }

    [Fact]
    public void CostIsOptionalAndUnknownPricesAreExplicitlyPartial()
    {
        var session = NewSession().Add(Card(value: 60)).Add(Card(value: null));
        Assert.Equal(60m, session.EstimatedValueBrl); Assert.Equal(1, session.UnpricedCards); Assert.True(session.IsPartialValuation);
        Assert.Null(session.CostBrl); Assert.Null(session.EstimatedDifferenceBrl); Assert.Null(session.EstimatedDifferencePercent);
        var free = session.ConfigureCost(null, PackCostMode.Total, 0); Assert.Equal(60m, free.EstimatedDifferenceBrl); Assert.Null(free.EstimatedDifferencePercent);
    }

    [Fact]
    public void SamePhysicalScanCannotBeCountedTwiceButDuplicateCardsAreAllowed()
    {
        var card = Card(); var session = NewSession().Add(card).Add(card);
        Assert.Single(session.Cards);
        session = session.Add(Card(card.PrintingId)); Assert.Equal(2, session.Cards.Count); Assert.Equal(40m, session.EstimatedValueBrl);
        Assert.Throws<InvalidOperationException>(() => session.Add(card with { VariantId = Guid.NewGuid() }));
        Assert.Equal(20m, session.Remove(card.ScanId).EstimatedValueBrl);
    }

    [Fact]
    public void ImportFreezesReviewedDataAndRecordsEachPhysicalCopyOnce()
    {
        var a = Card(); var b = Card(); var session = NewSession().Add(a).Add(b).Complete().BeginImport();
        Assert.Throws<InvalidOperationException>(() => session.Add(Card())); Assert.Throws<InvalidOperationException>(() => session.Remove(a.ScanId));
        var item = Guid.NewGuid(); session = session.RecordImported(a.ScanId, item);
        Assert.Equal(ScannerSessionPhase.Importing, session.Phase); Assert.Same(session, session.BeginImport());
        Assert.Throws<InvalidOperationException>(() => session.RecordImported(a.ScanId, Guid.NewGuid()));
        session = session.RecordImported(b.ScanId, Guid.NewGuid()); Assert.Equal(ScannerSessionPhase.Imported, session.Phase);
    }

    [Fact]
    public void InvalidCostsAndUnsourcedValuationAreRejected()
    {
        Assert.Throws<ArgumentException>(() => NewSession().ConfigureCost(null, PackCostMode.PerPack, 20));
        Assert.Throws<ArgumentException>(() => NewSession().ConfigureCost(5, PackCostMode.Total, -1));
        Assert.Throws<ArgumentException>(() => NewSession().ConfigureCost(5, PackCostMode.Total, 10.999m));
        Assert.Throws<ArgumentException>(() => NewSession().Add(Card() with { MarketValue = new(30, "", Now) }));
    }
}
