using Vaulta.App.Core.Catalog;
using Vaulta.Catalog.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class ScannerValuationTests
{
    [Fact]
    public void UnknownModelFinishCannotTakeTheOnlyCatalogVariantsPrice()
    {
        var details = Details();
        details = details with { Printing = details.Printing with { Variants = [details.Printing.Variants[0]] } };
        var selected = ScannerValuation.SelectVariant(details, Visual());
        Assert.Null(selected);
        Assert.Null(ScannerValuation.ChooseQuote(details, selected?.Id, Visual()));
    }

    [Fact]
    public void ConfirmedReverseAddsItsOwnQuoteAndPersistsVisualReading()
    {
        var details = Details(); var visual = Visual() with { Finish = "reverse" };
        var variant = Assert.IsType<CatalogVariantDto>(ScannerValuation.SelectVariant(details, visual));
        var quote = Assert.IsType<CardMarketQuoteDto>(ScannerValuation.ChooseQuote(details, variant.Id, visual));
        var card = new ScannerSessionCard(Guid.NewGuid(), details.Printing.PrintingId, variant.Id, "Golisopod", "Set", "026/086",
            variant.Name, "UNKNOWN", null, new(quote.MarketValueBrl, quote.Source, quote.UpdatedAt), DateTimeOffset.UtcNow, VisualIdentification: visual);
        var session = ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow).Add(card).Add(card);
        Assert.Equal(1.25m, session.EstimatedValueBrl);
        Assert.Single(session.Cards);
        Assert.Equal(visual, session.Cards[0].VisualIdentification);
    }

    [Fact]
    public void UnknownFinishShowsAvailablePricesWithoutChoosingTheWrongVariant()
    {
        var details = Details();
        Assert.Null(ScannerValuation.SelectVariant(details, Visual()));
        Assert.Equal(.12m, ScannerValuation.ChooseQuote(details, details.Printing.Variants[0].Id)!.MarketValueBrl);
        Assert.Equal(1.25m, ScannerValuation.ChooseQuote(details, details.Printing.Variants[1].Id)!.MarketValueBrl);
    }

    [Fact]
    public void ConflictingFinishDoesNotSilentlySelectOnlyCatalogVariant()
    {
        var details = Details();
        details = details with { Printing = details.Printing with { Variants = [details.Printing.Variants[0]] } };
        Assert.Null(ScannerValuation.SelectVariant(details, Visual() with { Finish = "reverse" }));
    }

    [Fact]
    public void GradingLabelIsPreservedButRawQuoteIsNotAGradedPrice()
    {
        var details = Details(); var visual = Visual() with { Certification = new("PSA", "10", "01234567") };
        Assert.Null(ScannerValuation.ChooseQuote(details, details.Printing.Variants[0].Id, visual));
        Assert.False(visual.Certification!.Verified);
        Assert.Contains("01234567", ScannerValuation.CertificationNotes(visual));
    }

    private static CardVisualIdentificationDto Visual() => new("Golisopod", "026/086", "pt-BR", null, .98);
    private static ScannerCardDetailsDto Details()
    {
        CatalogVariantDto[] variants = [new(Guid.NewGuid(), "normal", "Normal"), new(Guid.NewGuid(), "reverse", "Reverse holo")];
        var now = DateTimeOffset.UtcNow;
        return new(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "pokemon", "Set", "Golisopod", "026/086", "pt", null, null, variants),
            new Dictionary<string, string>(), variants.Select((v, i) => new CardMarketQuoteDto(v.Id, v.Name, i == 0 ? .12m : 1.25m,
                1m, "USD", "TCGplayer · PTAX", now, 5m, now, [])).ToArray(), null);
    }
}
