using Vaulta.Catalog.Application;
using Vaulta.Catalog.Domain;
using Vaulta.Catalog.Infrastructure;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public sealed class CatalogDetailedPriceTests
{
    [Fact]
    public void DetailedQuoteBelongsOnlyToItsEditionAndCannotBeCopiedToFirstEdition()
    {
        var unlimited=new Variant { Id=Guid.NewGuid(),Code="holo-unlimited",Name="Holo unlimited",RawValue="{\"type\":\"holo\",\"subtype\":\"unlimited\",\"pricing\":{\"tcgplayer\":{\"unit\":\"USD\",\"updated\":\"2026-10-02T10:00:00Z\",\"holofoil\":{\"marketPrice\":10}}}}" };
        var first=new Variant { Id=Guid.NewGuid(),Code="holo-shadowless-first-edition",Name="First edition",RawValue="{\"type\":\"holo\",\"subtype\":\"shadowless\",\"stamp\":[\"1st-edition\"],\"pricing\":{\"tcgplayer\":null,\"cardmarket\":null}}" };
        var root="{\"tcgplayer\":{\"unit\":\"USD\",\"updated\":\"2026-10-02T10:00:00Z\",\"holofoil\":{\"marketPrice\":100}}}";
        var quotes=CatalogPriceParser.Read(root,[unlimited,first],new Dictionary<string,BrlExchangeRate> { ["USD"]=new("USD",6m,DateTimeOffset.UtcNow) });
        var quote=Assert.Single(quotes); Assert.Equal(unlimited.Id,quote.VariantId); Assert.Equal("Holo unlimited",quote.VariantName); Assert.Equal(60m,quote.MarketValueBrl);
    }
}
