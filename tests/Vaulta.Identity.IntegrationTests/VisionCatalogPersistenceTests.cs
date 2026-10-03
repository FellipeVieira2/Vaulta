using System.Text.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class VisionCatalogPersistenceTests(ApiFixture fixture)
{
    [Fact]
    public async Task SynchronizingTwicePreservesSeriesMetadataAndCanonicalIds()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var id=Guid.NewGuid().ToString("N");
        var set=JsonSerializer.Deserialize<ProviderSet>(JsonSerializer.Serialize(new { ExternalId=id,Name="Vision Set "+id,Code=(string?)null,ReleaseDate=(string?)null,Series=new { ExternalId="series-"+id,Name="Vision Series "+id } }))!;
        var input=JsonSerializer.Deserialize<ProviderPrinting>(JsonSerializer.Serialize(new { ExternalId="card-"+id,Name="Same named card",CollectorNumber="01/100",Language="en",Rarity="common",ImageUrl=(string?)null,Variants=new[]{new { Code="normal",Name="Normal",RawValue="normal" }},MetadataJson="{\"hp\":120,\"illustrator\":\"Artist\"}" }))!;
        var provider=new Source("vision-"+id,new(set,[input]));
        var sync=new CatalogSyncService(db,[provider],scope.ServiceProvider.GetRequiredService<IClock>(),NullLogger<CatalogSyncService>.Instance);
        await sync.Synchronize(provider.Code,id,default);
        var printing=await db.Printings.Include(x=>x.Set).SingleAsync(x=>x.Set.Name==set.Name);
        var original=printing.Id;
        Assert.NotNull(printing.Set.SeriesId);
        var serialized=JsonSerializer.SerializeToElement(printing,new JsonSerializerOptions { ReferenceHandler=System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
        Assert.True(serialized.TryGetProperty("MetadataJson",out var metadata));
        using var stored=JsonDocument.Parse(metadata.GetString()!);
        Assert.Equal(120,stored.RootElement.GetProperty("hp").GetInt32());
        await sync.Synchronize(provider.Code,id,default);
        var again=await db.Printings.Include(x=>x.Set).SingleAsync(x=>x.Set.Name==set.Name);
        Assert.Equal(original,again.Id);
        Assert.Equal(1,await db.Series.CountAsync(x=>x.Name=="Vision Series "+id));
    }
    [Fact]
    public async Task InitialImportBatchesIdentityLookupsInsteadOfQueryingEveryCard()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();
        var original=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var counter=new ReadCounter();
        await using var db=new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(original.Database.GetConnectionString()).AddInterceptors(counter).Options);
        var id=Guid.NewGuid().ToString("N");
        var set=new ProviderSet(id,"Batch "+id,null,null);
        var cards=Enumerable.Range(1,20).Select(i=>new ProviderPrinting("card-"+id+"-"+i,"Card "+i,i+"/100","en",null,null,[new("normal","Normal","normal")])).ToArray();
        var source=new Source("batch-"+id,new(set,cards));
        await new CatalogSyncService(db,[source],scope.ServiceProvider.GetRequiredService<IClock>(),NullLogger<CatalogSyncService>.Instance).Synchronize(source.Code,id,default);
        Assert.Equal(20,await db.Printings.CountAsync(x=>x.Set.Name==set.Name));
        Assert.InRange(counter.Reads,1,18);
    }
    [Fact]
    public async Task FullImportRecordsFailedSetsAndResumeDoesNotDownloadCompletedSets()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var id=Guid.NewGuid().ToString("N");
        var good=new ProviderSet("good-"+id,"Good "+id,null,null);
        var bad=new ProviderSet("bad-"+id,"Bad "+id,null,null);
        var source=new RecoverableSource("resume-"+id,[good,bad]);
        var sync=new CatalogSyncService(db,[source],scope.ServiceProvider.GetRequiredService<IClock>(),NullLogger<CatalogSyncService>.Instance);
        var first=await sync.Synchronize(source.Code,"all",default);
        var run=await db.SyncRuns.SingleAsync(x=>x.Id==first);
        Assert.Equal("partial",run.Status);
        Assert.Equal(1,await db.Sets.CountAsync(x=>x.Name==good.Name));
        source.Fail=false;
        var resumed=await sync.Synchronize(source.Code,"resume:"+first,default);
        Assert.Equal("completed",(await db.SyncRuns.SingleAsync(x=>x.Id==resumed)).Status);
        Assert.Equal(1,source.Downloads[good.ExternalId]);
        Assert.Equal(2,source.Downloads[bad.ExternalId]);
        Assert.Equal(1,await db.Sets.CountAsync(x=>x.Name==bad.Name));
    }
    private sealed class RecoverableSource(string code,ProviderSet[] sets) : ICatalogProvider
    {
        public string Code=>code;
        public bool Fail=true;
        public Dictionary<string,int> Downloads=new();
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken ct)=>Task.FromResult<IReadOnlyList<ProviderSet>>(sets);
        public Task<ProviderSetDetails> GetSetDetails(string id,CancellationToken ct)
        {
            Downloads[id]=Downloads.GetValueOrDefault(id)+1;
            if(Fail && id.StartsWith("bad-")) throw new CatalogProviderException("Unavailable",true,"test_unavailable");
            return Task.FromResult(new ProviderSetDetails(sets.Single(x=>x.ExternalId==id),[]));
        }
    }
    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
        {
            if(command.CommandText.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref Reads);
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Source(string code,ProviderSetDetails details,string language="en") : ICatalogProvider
    {
        public string Code=>code;
        public string? Language=>language;
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken ct)=>Task.FromResult<IReadOnlyList<ProviderSet>>([details.Set]);
        public Task<ProviderSetDetails> GetSetDetails(string id,CancellationToken ct)=>Task.FromResult(details);
    }
    [Fact] public async Task MoreThanOneHundredMatchingReprintsCannotBecomeArtificialCertainty()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var name="Overflow "+Guid.NewGuid().ToString("N");var game=Guid.Parse("f18fd4d1-2514-4b19-9eaa-f33c04564c7b");
        var card=new Vaulta.Catalog.Domain.Card{Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name.ToLowerInvariant()};
        for(var i=0;i<101;i++)db.Printings.Add(new(){Id=Guid.NewGuid(),Card=card,Set=new(){Id=Guid.NewGuid(),GameId=game,Name=name+i,NormalizedName=(name+i).ToLowerInvariant()},CollectorNumber="1/100",NormalizedCollectorNumber="1/100",Language="en",MetadataJson="{\"hp\":90}"});
        await db.SaveChangesAsync();var evidence=new CardEvidence(new("pokemon",.9),new(name,.9),new(null,0),new(null,0),new(null,0),new("en",.9),new(null,0),"fixture","fixture",Hp:new("90",.9));
        await Assert.ThrowsAsync<Vaulta.Vision.Application.VisionEvidenceCandidateLimitException>(()=>new Vaulta.Vision.Infrastructure.VisionCatalog(db).FindEvidenceCandidatesAsync(evidence,default));
    }

    [Fact] public async Task StrongCollectorEvidenceUsesTheSamePaddingRulesAsTheResolver()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();var id=Guid.NewGuid().ToString("N");var name="Padding "+id;
        var cards=new[]{new ProviderPrinting("first-"+id,name,"026/86","en",null,null,[new("normal","Normal","normal")]),new ProviderPrinting("second-"+id,name,"25/86","en",null,null,[new("normal","Normal","normal")])};
        var source=new Source("padding-"+id,new(new(id,"Padding set "+id,null,null),cards));await new CatalogSyncService(db,[source],scope.ServiceProvider.GetRequiredService<IClock>(),NullLogger<CatalogSyncService>.Instance).Synchronize(source.Code,id,default);
        var evidence=new CardEvidence(new("pokemon",.95),new(name,.95),new("026/086",.95),new(null,0),new(null,0),new("en",.95),new("normal",.95),"fixture","fixture");
        var candidates=await new Vaulta.Vision.Infrastructure.VisionCatalog(db).FindEvidenceCandidatesAsync(evidence,default);Assert.Equal("026/86",Assert.Single(candidates).Printing.CollectorNumber);
    }

    [Fact] public async Task BrazilianPortugueseEvidenceFindsProviderPortugueseButRejectsExplicitOtherRegion()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var name="Portuguese "+Guid.NewGuid().ToString("N");var game=Guid.Parse("f18fd4d1-2514-4b19-9eaa-f33c04564c7b");
        var card=new Vaulta.Catalog.Domain.Card{Id=Guid.NewGuid(),GameId=game,Name=name,NormalizedName=name.ToLowerInvariant()};
        foreach(var language in new[]{"pt","pt-PT"}) db.Printings.Add(new(){Id=Guid.NewGuid(),Card=card,Set=new(){Id=Guid.NewGuid(),GameId=game,Name=name+language,NormalizedName=(name+language).ToLowerInvariant()},CollectorNumber="026/86",NormalizedCollectorNumber="026/86",Language=language,MetadataJson="{}"});
        await db.SaveChangesAsync();var evidence=new CardEvidence(new("pokemon",.95),new(name,.95),new("026/086",.95),new(null,0),new(null,0),new("pt-BR",.95),new(null,0),"fixture","fixture");
        var candidates=await new Vaulta.Vision.Infrastructure.VisionCatalog(db).FindEvidenceCandidatesAsync(evidence,default);
        Assert.Equal("pt",Assert.Single(candidates).Printing.Language);
        Assert.NotNull(new Vaulta.Vision.Application.VisionPrintingResolver().Resolve(candidates,evidence).PrintingId);
    }

    [Fact] public async Task RegionalCaseDoesNotLeaveRemovedPrintingsActive()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();var id=Guid.NewGuid().ToString("N");
        var card=new ProviderPrinting("card-"+id,"Region "+id,"1/100","pt-BR",null,null,[new("normal","Normal","normal")]);var set=new ProviderSet(id,"Region set "+id,null,null);
        var source=new Source("region-"+id,new(set,[card]),"pt-br");var clock=scope.ServiceProvider.GetRequiredService<IClock>();
        await new CatalogSyncService(db,[source],clock,NullLogger<CatalogSyncService>.Instance).Synchronize(source.Code,id,default);
        await new CatalogSyncService(db,[new Source(source.Code,new(set,[]),"pt-br")],clock,NullLogger<CatalogSyncService>.Instance).Synchronize(source.Code,id,default);
        Assert.False(await db.Printings.Where(x=>x.Card.Name==card.Name).Select(x=>x.IsActive).SingleAsync());
    }
    [Fact] public async Task LocalizedSetUpdateKeepsItsEarlierVisibleNameResolvable()
    {
        await using var scope=fixture.Factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<CatalogDbContext>();var id=Guid.NewGuid().ToString("N");var name="Aliases "+id;var english="English set "+id;
        var card=new ProviderPrinting("card-"+id,name,"1/100","en",null,null,[new("normal","Normal","normal")]);var clock=scope.ServiceProvider.GetRequiredService<IClock>();
        await new CatalogSyncService(db,[new Source("tcgdex",new(new(id,english,null,null),[card]))],clock,NullLogger<CatalogSyncService>.Instance).Synchronize("tcgdex",id,default);
        await new CatalogSyncService(db,[new Source("tcgdex",new(new(id,"Portuguese set "+id,null,null),[card with {Language="pt"}]),"pt")],clock,NullLogger<CatalogSyncService>.Instance).Synchronize("tcgdex",id,default);
        var evidence=new CardEvidence(new("pokemon",.95),new(name,.95),new("1/100",.95),new(null,0),new(english,.95),new("en",.95),new("normal",.95),"fixture","fixture");
        var candidates=await new Vaulta.Vision.Infrastructure.VisionCatalog(db).FindEvidenceCandidatesAsync(evidence,default);
        Assert.NotNull(new Vaulta.Vision.Application.VisionPrintingResolver().Resolve(candidates,evidence).PrintingId);
    }

}