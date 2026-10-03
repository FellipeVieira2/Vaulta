using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Catalog.Domain;
using Vaulta.SharedKernel;
using Xunit;
namespace Vaulta.Identity.IntegrationTests;
[Collection("api")]
public sealed class LocalVisionQuoteTests(ApiFixture fixture)
{
    [Fact]
    public async Task LocalDetailsReturnsStoredQuoteEvenWhenExpiredWithoutRequestingProviders()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var printing=await Seed(db);
        var details=await scope.ServiceProvider.GetRequiredService<ICatalogSearch>().GetPrinting(printing.Id,default);
        var variant=printing.Variants.Single(); var now=DateTimeOffset.UtcNow;
        var quote=new CardMarketQuoteDto(variant.Id,"Normal",42m,7m,"USD","Fixture market",now.AddDays(-2),6m,now.AddDays(-2),[]);
        db.DailyMarketSnapshots.Add(new DailyCardMarketSnapshot { PrintingId=printing.Id,MarketDay=DateOnly.FromDateTime(now.AddDays(-2).UtcDateTime),FetchedAt=now.AddDays(-2),RefreshAfter=now.AddDays(-1),Payload=JsonSerializer.Serialize(new ScannerCardDetailsDto(details!,new Dictionary<string,string>(),[quote],null)) });
        await db.SaveChangesAsync();
        var result=await new LocalScannerDetailsReader(db,scope.ServiceProvider.GetRequiredService<ICatalogSearch>(),scope.ServiceProvider.GetRequiredService<IClock>()).GetAsync(printing.Id,default);
        Assert.Equal(42m,Assert.Single(result!.MarketQuotes).MarketValueBrl);
        Assert.Contains("atualização",result.Notice); Assert.Equal("120",result.Information["Pontos de vida"]);
    }
    [Fact]
    public async Task KnownPrintingWithoutQuoteRemainsIdentifiedWithExplicitUnavailablePrice()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>(); var printing=await Seed(db);
        var result=await new LocalScannerDetailsReader(db,scope.ServiceProvider.GetRequiredService<ICatalogSearch>(),scope.ServiceProvider.GetRequiredService<IClock>()).GetAsync(printing.Id,default);
        Assert.Equal(printing.Id,result!.Printing.PrintingId); Assert.Empty(result.MarketQuotes); Assert.Contains("indisponível",result.Notice);
    }
    private static async Task<Printing> Seed(CatalogDbContext db)
    {
        var name="Local "+Guid.NewGuid().ToString("N"); var game=Guid.Parse("f18fd4d1-2514-4b19-9eaa-f33c04564c7b");
        var set=new Set { Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name.ToLowerInvariant() };
        var card=new Card { Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name.ToLowerInvariant() };
        var printing=new Printing { Id=Guid.NewGuid(),Card=card,Set=set,CollectorNumber="1",NormalizedCollectorNumber="1",Language="en",MetadataJson="{\"hp\":120}",Variants=[new Variant { Id=Guid.NewGuid(),Code="normal",Name="Normal" }] };
        db.Printings.Add(printing); await db.SaveChangesAsync(); return printing;
    }
}
