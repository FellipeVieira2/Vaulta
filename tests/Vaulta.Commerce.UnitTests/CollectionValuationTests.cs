using Vaulta.Catalog.Contracts;
using Vaulta.Collection.Application;
using Xunit;

namespace Vaulta.Commerce.UnitTests;

public sealed class CollectionValuationTests
{
    private static readonly DateTimeOffset Now = new TestClock().UtcNow;
    private static CardMarketQuoteDto Quote(Guid variant, decimal price, decimal? average = null) => new(variant, "Normal", price, price / 5,
        "USD", "TCGplayer", Now, 5, Now, average.HasValue ? [new(7, average.Value, 999, 999)] : []);

    [Fact]
    public async Task PhysicalQuantitiesAndExactVariantsDetermineTotalAndWeightedReaisComparison()
    {
        var printing = Guid.NewGuid(); var variant = Guid.NewGuid(); var other = Guid.NewGuid();
        var source = new Source([new(Guid.NewGuid(), printing, variant, 3), new(Guid.NewGuid(), printing, other, 2)],
            new Dictionary<Guid, IReadOnlyList<CardMarketQuoteDto>> { [printing] = [Quote(variant, 60, 40), Quote(other, 100)] });
        var result = await new CollectionValuationService(source, new TestClock(), CollectionValuationLimits.Default).Get(Guid.NewGuid(), null, CancellationToken.None);
        Assert.Equal("BRL", result.Currency); Assert.Equal(380m, result.EstimatedValueBrl); Assert.Equal(5, result.PricedItems); Assert.Equal(0, result.UnpricedItems);
        Assert.Equal(1, source.Calls); var period = Assert.Single(result.Comparisons);
        Assert.Equal(120m, period.AverageBrl); Assert.Equal(180m, period.CurrentReferenceValueBrl); Assert.Equal(60m, period.DifferenceBrl);
        Assert.Equal(50m, period.DifferencePercent); Assert.Equal(3, period.ComparedItems); // never trust supplied percentages or mix unmatched coverage
    }

    [Fact]
    public async Task UnknownVariantAndMissingQuotesRemainExplicitlyUnpricedInsteadOfZeroPricedOrGuessed()
    {
        var printing = Guid.NewGuid(); var variant = Guid.NewGuid();
        var source = new Source([new(Guid.NewGuid(), printing, null, 2), new(Guid.NewGuid(), printing, variant, 3), new(Guid.NewGuid(), Guid.NewGuid(), variant, 4)],
            new Dictionary<Guid, IReadOnlyList<CardMarketQuoteDto>> { [printing] = [Quote(variant, 0)] });
        var result = await new CollectionValuationService(source, new TestClock(), CollectionValuationLimits.Default).Get(Guid.NewGuid(), null, CancellationToken.None);
        Assert.Equal(0m, result.EstimatedValueBrl); Assert.Equal(9, result.TotalItems); Assert.Equal(3, result.PricedItems); Assert.Equal(6, result.UnpricedItems); Assert.True(result.IsPartial);
    }

    [Fact]
    public async Task ForeignPriceRelabelledAsReaisAndUnsourcedPricesCannotEnterTotal()
    {
        var printing = Guid.NewGuid(); var variant = Guid.NewGuid();
        var source = new Source([new(Guid.NewGuid(), printing, variant, 1)], new Dictionary<Guid, IReadOnlyList<CardMarketQuoteDto>>
        { [printing] = [Quote(variant, 20) with { OriginalValue = 20, ExchangeRate = 5 }, Quote(variant, 20) with { Source = "" }] });
        var result = await new CollectionValuationService(source, new TestClock(), CollectionValuationLimits.Default).Get(Guid.NewGuid(), null, CancellationToken.None);
        Assert.Equal(1, result.UnpricedItems); Assert.Equal(0m, result.EstimatedValueBrl);
    }

    [Fact]
    public async Task TimeoutDoesNotDiscardOtherPricedCardsAndCallerCancellationPropagates()
    {
        var slow = Guid.NewGuid(); var fast = Guid.NewGuid(); var variant = Guid.NewGuid();
        var source = new Source([new(Guid.NewGuid(), slow, variant, 2), new(Guid.NewGuid(), fast, variant, 3)],
            new Dictionary<Guid, IReadOnlyList<CardMarketQuoteDto>> { [fast] = [Quote(variant, 10)] }) { Slow = slow };
        var result = await new CollectionValuationService(source, new TestClock(), new(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(20)))
            .Get(Guid.NewGuid(), null, CancellationToken.None);
        Assert.True(result.RefreshIncomplete); Assert.Equal(30m, result.EstimatedValueBrl); Assert.Equal(2, result.UnpricedItems);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CollectionValuationService(source, new TestClock(), CollectionValuationLimits.Default).Get(Guid.NewGuid(), null, cts.Token));
    }

    private sealed class Source(IReadOnlyList<CollectionValuationPosition> positions, IReadOnlyDictionary<Guid, IReadOnlyList<CardMarketQuoteDto>> quotes) : ICollectionValuationSource
    {
        public int Calls { get; private set; }
        public Guid? Slow { get; init; }
        public Task<IReadOnlyList<CollectionValuationPosition>> GetPositions(Guid owner, Guid? entryId, CancellationToken ct) => Task.FromResult(positions);
        public async Task<IReadOnlyList<CardMarketQuoteDto>> GetQuotes(Guid printingId, CancellationToken ct)
        {
            Calls++;
            if (Slow == printingId) await Task.Delay(Timeout.Infinite, ct);
            return quotes.GetValueOrDefault(printingId) ?? [];
        }
    }
}
