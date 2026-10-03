using System.Text.Json;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class ScannerPricingTests
{
    [Fact]
    public void MarketQuotes_ConvertCurrentAndPeriodAveragesToBrlForExactVariant()
    {
        using var data = JsonDocument.Parse("""
            {"pricing":{"cardmarket":{"unit":"EUR","updated":"2026-09-30T15:00:00Z","trend":10,"avg1":9,"avg7":8,"avg30":5,"trend-holo":40,"avg7-holo":20}}}
            """);
        var normal = new CatalogVariantDto(Guid.NewGuid(), "normal", "Normal");
        var reverse = new CatalogVariantDto(Guid.NewGuid(), "reverse", "Reverse");
        var rates = new Dictionary<string, BrlExchangeRate> { ["EUR"] = new("EUR", 6m, DateTimeOffset.Parse("2026-09-30T16:00:00Z")) };
        var quotes = TcgDexScannerDetailsReader.ReadQuotes(data.RootElement, [normal, reverse], rates);
        var quote = Assert.Single(quotes);
        Assert.Equal(normal.Id, quote.VariantId);
        Assert.Equal(60m, quote.MarketValueBrl);
        Assert.Equal(10m, quote.OriginalValue);
        Assert.Equal("EUR", quote.OriginalCurrency);
        Assert.Equal(48m, quote.AverageMarketValueBrl);
        Assert.Equal(7, quote.AveragePeriodDays);
        var week = Assert.Single(quote.Comparisons, x => x.Days == 7);
        Assert.Equal(48m, week.AverageBrl);
        Assert.Equal(12m, week.DifferenceBrl);
        Assert.Equal(25m, week.DifferencePercent);
    }

    [Fact]
    public void MissingExchangeRate_DoesNotRelabelForeignAmountAsReais()
    {
        using var data = JsonDocument.Parse("""{"pricing":{"cardmarket":{"unit":"EUR","updated":"2026-09-30T15:00:00Z","trend":10}}}""");
        Assert.Empty(TcgDexScannerDetailsReader.ReadQuotes(data.RootElement, [new(Guid.NewGuid(), "normal", "Normal")], new Dictionary<string, BrlExchangeRate>()));
    }

    [Fact]
    public void Information_PreservesAttacksAndAbilitiesWithoutExposingRawPriceOrArtworkFields()
    {
        using var data = JsonDocument.Parse("""{"name":"Pikachu","hp":60,"attacks":[{"name":"Thunderbolt","damage":60,"effect":"Discard energy"}],"abilities":[{"name":"Static","effect":"Paralyze"}],"image":"https://assets.test/card","pricing":{},"id":"base1-58"}""");
        var info = TcgDexScannerDetailsReader.ReadInformation(data.RootElement);
        Assert.Equal("Pikachu", info["Nome"]);
        Assert.Contains("Thunderbolt", info["Ataques"]);
        Assert.Contains("Paralyze", info["Habilidades"]);
        Assert.DoesNotContain("pricing", info.Keys);
        Assert.DoesNotContain("image", info.Keys);
    }
    [Fact] public void NextProviderObservationKeepsSeparatelyPricedDetailedEditions()
    {
        using var data=JsonDocument.Parse("""{"pricing":{"cardmarket":{"unit":"EUR","updated":"2026-10-03T00:00:00Z","trend-holo":999}},"variants_detailed":[{"type":"holo","subtype":"unlimited","pricing":{"cardmarket":{"unit":"EUR","updated":"2026-10-03T00:00:00Z","trend-holo":10}}},{"type":"holo","subtype":"shadowless","pricing":{"cardmarket":{"unit":"EUR","updated":"2026-10-03T00:00:00Z","trend-holo":30}}}]}""");
        var unlimited=new CatalogVariantDto(Guid.NewGuid(),"holo-unlimited","Unlimited");var shadowless=new CatalogVariantDto(Guid.NewGuid(),"holo-shadowless","Shadowless");
        var quotes=TcgDexScannerDetailsReader.ReadQuotes(data.RootElement,[unlimited,shadowless],new Dictionary<string,BrlExchangeRate>{{"EUR",new("EUR",6m,DateTimeOffset.UtcNow)}});
        Assert.Equal(60m,Assert.Single(quotes,x=>x.VariantId==unlimited.Id).MarketValueBrl);Assert.Equal(180m,Assert.Single(quotes,x=>x.VariantId==shadowless.Id).MarketValueBrl);
    }

}
