using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vaulta.Assets.Application;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Domain;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Catalog.Infrastructure.Artwork;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class CatalogArtworkImportTests(ApiFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SecondImportKeepsAssetIdentityUsingValidatorOrDownloadedHash(bool validators)
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var printing=await Seed(db); var originalId=printing.Id; var setId=printing.SetId;
        using var image=new Image<Rgb24>(30,40); using var output=new MemoryStream(); image.SaveAsPng(output); var bytes=output.ToArray();
        var handler=new Handler(bytes,validators); using var http=new HttpClient(handler); var assets=new Assets();
        var options=Options.Create(new ArtworkImportOptions());
        var importer=new CatalogArtifactImporter(new AssetScopes(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),assets),new(http),new Rates(),scope.ServiceProvider.GetRequiredService<IClock>(),options,NullLogger<CatalogArtifactImporter>.Instance);
        Assert.Equal(1,(await importer.ImportAsync(setId,default)).Ready);
        var first=await db.Printings.AsNoTracking().SingleAsync(x=>x.Id==originalId);
        Assert.NotNull(first.ArtworkAssetId); Assert.NotNull(first.ArtworkSha256);
        // Recheck expiry must still exercise validators/hash; freshness must not be extended by a skipped load.
        await db.Printings.Where(x=>x.Id==originalId).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.ArtworkCheckedAt,DateTimeOffset.UtcNow.AddDays(-2)));
        Assert.Equal(1,(await importer.ImportAsync(setId,default)).Unchanged);
        var second=await db.Printings.AsNoTracking().SingleAsync(x=>x.Id==originalId);
        Assert.Equal(first.ArtworkAssetId,second.ArtworkAssetId); Assert.Equal(2,assets.Puts); Assert.Equal(2,handler.Requests);
        Assert.Equal(validators ? 1 : 2,handler.Bodies);
        var details=await scope.ServiceProvider.GetRequiredService<ICatalogSearch>().GetPrinting(originalId,default);
        Assert.Equal($"/api/v1/catalog/printings/{originalId}/artwork",details!.ArtworkUrl);
    }

    [Fact]
    public async Task ChangedSourceHashReplacesArtworkButRetainsCanonicalPrinting()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>(); var printing=await Seed(db);
        using var image=new Image<Rgb24>(30,40); using var output=new MemoryStream(); image.SaveAsPng(output);
        var handler=new Handler(output.ToArray(),false); using var http=new HttpClient(handler); var assets=new Assets();
        var options=Options.Create(new ArtworkImportOptions());
        var importer=new CatalogArtifactImporter(new AssetScopes(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),assets),new(http),new Rates(),scope.ServiceProvider.GetRequiredService<IClock>(),options,NullLogger<CatalogArtifactImporter>.Instance);
        await importer.ImportAsync(printing.SetId,default); var first=await db.Printings.AsNoTracking().SingleAsync(x=>x.Id==printing.Id);
        await db.Printings.Where(x=>x.Id==printing.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.ArtworkCheckedAt,DateTimeOffset.UtcNow.AddDays(-2)));
        image[0,0]=new Rgb24(200,30,70); using var changed=new MemoryStream(); image.SaveAsPng(changed); handler.Bytes=changed.ToArray();
        Assert.Equal(1,(await importer.ImportAsync(printing.SetId,default)).Ready);
        var second=await db.Printings.AsNoTracking().SingleAsync(x=>x.Id==printing.Id);
        Assert.NotEqual(first.ArtworkSha256,second.ArtworkSha256); Assert.NotEqual(first.ArtworkAssetId,second.ArtworkAssetId);
    }

    [Fact]
    public async Task RecentReadyArtworkIsReusedWithoutAnotherRequestOrMovingItsRevalidationDeadline()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();var printing=await Seed(db);
        using var image=new Image<Rgb24>(30,40);using var output=new MemoryStream();image.SaveAsPng(output);
        var handler=new Handler(output.ToArray(),false);using var http=new HttpClient(handler);var assets=new Assets();
        var options=Options.Create(new ArtworkImportOptions());
        var importer=new CatalogArtifactImporter(new AssetScopes(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),assets),new(http),new Rates(),scope.ServiceProvider.GetRequiredService<IClock>(),options,NullLogger<CatalogArtifactImporter>.Instance);
        await importer.ImportAsync(printing.SetId,default);var first=await db.Printings.AsNoTracking().SingleAsync(x=>x.Id==printing.Id);
        Assert.Equal(1,(await importer.ImportAsync(printing.SetId,default)).Unchanged);
        var second=await db.Printings.AsNoTracking().SingleAsync(x=>x.Id==printing.Id);
        Assert.Equal(1,handler.Requests);Assert.Equal(2,assets.Puts);Assert.Equal(first.ArtworkCheckedAt,second.ArtworkCheckedAt);
    }

    [Fact]
    public async Task ParallelWorkersPersistEveryAvailableQuote()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var first=await Seed(db);
        var printings=new List<Printing>{first};
        for(var i=2;i<=24;i++)
            printings.Add(new Printing{Id=Guid.NewGuid(),CardId=first.CardId,SetId=first.SetId,CollectorNumber=i.ToString(),NormalizedCollectorNumber=i.ToString(),Language="en",ExternalArtworkUrl=first.ExternalArtworkUrl});
        foreach(var printing in printings)
        {
            printing.SourcePricingJson="{\"tcgplayer\":{\"unit\":\"USD\",\"updated\":\"2026-10-03T10:00:00Z\",\"normal\":{\"marketPrice\":1}}}";
            printing.Variants.Add(new Variant{Id=Guid.NewGuid(),Code="normal",Name="Normal"});
        }
        db.Printings.AddRange(printings.Skip(1));await db.SaveChangesAsync();
        using var image=new Image<Rgb24>(30,40);using var output=new MemoryStream();image.SaveAsPng(output);
        using var http=new HttpClient(new Handler(output.ToArray(),false));var assets=new Assets();
        var importer=new CatalogArtifactImporter(new AssetScopes(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),assets),new(http),new AvailableRates(),scope.ServiceProvider.GetRequiredService<IClock>(),Options.Create(new ArtworkImportOptions{WorkerCount=8}),NullLogger<CatalogArtifactImporter>.Instance);
        var result=await importer.ImportAsync(first.SetId,default);
        Assert.Equal(0,result.Failed);Assert.Equal(24,result.Ready);Assert.Equal(24,result.Priced);
        var ids=printings.Select(x=>x.Id).ToArray();
        Assert.Equal(24,await db.DailyMarketSnapshots.CountAsync(x=>ids.Contains(x.PrintingId)));
    }
    [Fact]
    public async Task ProviderTimeoutMarksOnlyItsCardFailedAndContinuesTheQueue()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();var first=await Seed(db);
        db.Printings.AddRange(Enumerable.Range(2,2).Select(i=>new Printing{Id=Guid.NewGuid(),CardId=first.CardId,SetId=first.SetId,CollectorNumber=i.ToString(),NormalizedCollectorNumber=i.ToString(),Language="en",ExternalArtworkUrl=first.ExternalArtworkUrl}));await db.SaveChangesAsync();
        using var image=new Image<Rgb24>(30,40);using var output=new MemoryStream();image.SaveAsPng(output);
        using var http=new HttpClient(new Handler(output.ToArray(),false){Timeouts=1});var assets=new Assets();
        var importer=new CatalogArtifactImporter(new AssetScopes(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),assets),new(http),new Rates(),scope.ServiceProvider.GetRequiredService<IClock>(),Options.Create(new ArtworkImportOptions{WorkerCount=1}),NullLogger<CatalogArtifactImporter>.Instance);
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(10));var result=await importer.ImportAsync(first.SetId,budget.Token);
        Assert.Equal(1,result.Failed);Assert.Equal(2,result.Ready);
    }
    private sealed class AvailableRates : IBrlExchangeRateProvider
    { public Task<BrlExchangeRate?> GetAsync(string currency,CancellationToken ct)=>Task.FromResult<BrlExchangeRate?>(new(currency,5m,DateTimeOffset.UtcNow)); }

    private static async Task<Printing> Seed(CatalogDbContext db)
    {
        var name="Artwork "+Guid.NewGuid().ToString("N"); var game=Guid.Parse("f18fd4d1-2514-4b19-9eaa-f33c04564c7b");
        var printing=new Printing { Id=Guid.NewGuid(),Card=new Card { Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name.ToLowerInvariant() },Set=new Set { Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name.ToLowerInvariant() },CollectorNumber="1",NormalizedCollectorNumber="1",Language="en",ExternalArtworkUrl="https://assets.tcgdex.net/en/base/base1/1/high.png" };
        db.Printings.Add(printing); await db.SaveChangesAsync(); return printing;
    }

    private sealed class Rates : IBrlExchangeRateProvider
    { public Task<BrlExchangeRate?> GetAsync(string currency,CancellationToken ct)=>Task.FromResult<BrlExchangeRate?>(null); }

    private sealed class Handler(byte[] bytes,bool validators) : HttpMessageHandler
    {
        public byte[] Bytes=bytes; public int Requests; public int Bodies; public int Timeouts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            if(Interlocked.Decrement(ref Timeouts)>=0) throw new TaskCanceledException("Provider timeout.");
            if(validators && request.Headers.IfNoneMatch.Any()) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            Interlocked.Increment(ref Bodies); var result=new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(Bytes) };
            if(validators) result.Headers.ETag=new EntityTagHeaderValue("\"fixture-1\""); return Task.FromResult(result);
        }
    }

    private sealed class AssetScopes(IServiceScopeFactory source,ISystemAssetService assets) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()=>new AssetScope(source.CreateScope(),assets);
        private sealed class AssetScope(IServiceScope source,ISystemAssetService assets) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; }=new AssetProvider(source.ServiceProvider,assets);
            public void Dispose()=>source.Dispose();
        }
        private sealed class AssetProvider(IServiceProvider source,ISystemAssetService assets) : IServiceProvider
        {
            public object? GetService(Type serviceType)=>serviceType==typeof(ISystemAssetService)?assets:source.GetService(serviceType);
        }
    }

    private sealed class Assets : ISystemAssetService
    {
        public int Puts;
        public Task<Guid> StoreArtworkAsync(byte[] bytes,int width,int height,string source,bool thumbnail,CancellationToken ct)
        { Interlocked.Increment(ref Puts); return Task.FromResult(new Guid(System.Security.Cryptography.SHA256.HashData(bytes).AsSpan(0,16))); }
        public Task<string?> GetArtworkReadUrlAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<byte[]?> ReadArtworkAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
    }
}