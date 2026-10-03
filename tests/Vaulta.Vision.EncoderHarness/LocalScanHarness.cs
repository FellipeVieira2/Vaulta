using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Assets.Application;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure;
using Vaulta.SharedKernel;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Encoding;
using Vaulta.Vision.Infrastructure;
internal static class LocalScanHarness
{
    public static async Task<object> RunAsync(OnnxImageEncoder encoder,string images)
    {
        var connection=Environment.GetEnvironmentVariable("VAULTA_VISION_HARNESS_DB")??throw new InvalidOperationException("Supply the isolated validation database.");
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:Vaulta"]=connection }).Build();
        var services=new ServiceCollection(); services.AddLogging(); var clock=new Clock(); services.AddSingleton<IClock>(clock); services.AddCatalogModule(configuration);
        await using var provider=services.BuildServiceProvider(); await using var scope=provider.CreateAsyncScope();
        var catalogDb=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await using var vision=new VisionDbContext(new DbContextOptionsBuilder<VisionDbContext>().UseNpgsql(connection).Options);
        var index=new CosineReferenceIndex(encoder.Identity);
        await new VisualReferenceBuilder(vision,catalogDb,new NoAssetReads(),encoder,index,clock).LoadAsync(default);
        var service=new VisionScannerService(encoder,index,new VisionCatalog(catalogDb),new FixedEvidence(),scope.ServiceProvider.GetRequiredService<IScannerCardDetailsReader>(),new());
        var result=await service.IdentifyAsync(new([new(await File.ReadAllBytesAsync(Path.Combine(images,"base1-1.png")))]),default);
        var expected=await catalogDb.Printings.Where(x=>x.IsActive && x.Card.Name=="Alakazam" && x.CollectorNumber=="1/102" && x.Language=="en").Select(x=>x.Id).SingleAsync();
        if(result.Status!="identified" || result.PrintingId!=expected || result.PriceStatus!="available" || result.Price is null || result.Price.MarketValueBrl<=0)
            throw new InvalidOperationException("Real encoder/local canonical printing/variant price flow failed.");
        return new { kind="real-encoder-local-db-with-explicit-stub-evidence-not-physical-photo-benchmark",result };
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow=>DateTimeOffset.UtcNow; }
    private sealed class FixedEvidence : IVisionEvidenceReader
    {
        public Task<VisionEvidenceReading> ReadAsync(byte[] image,IReadOnlyList<VisionCatalogPrinting> candidates,CancellationToken ct)
        {
            CardEvidenceField F(string? value)=>new(value,value is null?0:.99);
            var evidence=new CardEvidence(F("pokemon"),F("Alakazam"),F("1/102"),F(null),F(null),F("en"),F("holo"),"explicit-integration-fixture","explicit-integration-fixture",F("80"),
                CardSide:F("front"),IsCard:F("true"),Edition:F("unlimited"));
            return Task.FromResult(new VisionEvidenceReading(evidence,null,null));
        }
    }
    // Loading/searching an existing DB index must not download any artwork or provider data.
    private sealed class NoAssetReads : ISystemAssetService
    {
        public Task<byte[]?> ReadArtworkAsync(Guid id,CancellationToken ct)=>throw new InvalidOperationException("Scan may not download artwork.");
        public Task<Guid> StoreArtworkAsync(byte[] image,int w,int h,string source,bool thumb,CancellationToken ct)=>throw new InvalidOperationException("Scan may not import artwork.");
        public Task<string?> GetArtworkReadUrlAsync(Guid id,CancellationToken ct)=>throw new InvalidOperationException("No artwork URL needed for matching.");
    }
}
