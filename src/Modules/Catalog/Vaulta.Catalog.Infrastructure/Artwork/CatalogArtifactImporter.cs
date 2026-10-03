using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using Vaulta.Assets.Application;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.SharedKernel;
namespace Vaulta.Catalog.Infrastructure.Artwork;
// Finite operator ingestion. No camera/request endpoint invokes this class.
public sealed class CatalogArtifactImporter(CatalogDbContext db,CatalogArtworkDownloader download,ISystemAssetService assets,
    ICatalogSearch catalog,IBrlExchangeRateProvider exchange,IClock clock,ILogger<CatalogArtifactImporter> logger) : ICatalogArtifactImporter
{
    public async Task<CatalogArtifactImportReport> ImportAsync(Guid? setId,CancellationToken ct)
    {
        // Serialize content-addressed asset upserts across operator processes.
        await db.Database.OpenConnectionAsync(ct);
        var key=BitConverter.ToInt64(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("vaulta:catalog:artifacts")),0);
        var connection=db.Database.GetDbConnection();
        await using var acquire=connection.CreateCommand(); acquire.CommandText="SELECT pg_try_advisory_lock(@key)";
        var parameter=acquire.CreateParameter(); parameter.ParameterName="key"; parameter.Value=key; acquire.Parameters.Add(parameter);
        var locked=(bool)(await acquire.ExecuteScalarAsync(ct) ?? false);
        if(!locked) { await db.Database.CloseConnectionAsync(); throw new InvalidOperationException("Catalog artifact import already running."); }
        var ready=0; var unchanged=0; var missing=0; var failed=0; var priced=0;
        try
        {
            var rates=new Dictionary<string,BrlExchangeRate>();
            foreach(var currency in new[]{"USD","EUR"})
            {
                try { var rate=await exchange.GetAsync(currency,ct); if(rate is not null) rates[currency]=rate; }
                catch(Exception error) when(error is HttpRequestException or JsonException) { logger.LogWarning("FX unavailable: {Currency} {ErrorType}",currency,error.GetType().Name); }
            }
            Guid? after=null;
            while(true)
            {
                var page=await db.Printings.Include(x=>x.Variants).Where(x=>x.IsActive && (setId==null || x.SetId==setId) && (after==null || x.Id.CompareTo(after.Value)>0))
                    .OrderBy(x=>x.Id).Take(100).ToArrayAsync(ct);
                if(page.Length==0) break;
                foreach(var printing in page)
                {
                    ct.ThrowIfCancellationRequested();
                    var recent=printing.ArtworkImportStatus=="ready" && printing.ArtworkAssetId is not null && printing.ThumbnailAssetId is not null && printing.ArtworkSha256 is not null && printing.ArtworkCheckedAt is { } checkedAt && checkedAt+TimeSpan.FromHours(24)>clock.UtcNow;
                    try
                    {
                        if(recent) { unchanged++; }
                        else if(printing.ExternalArtworkUrl is null) { printing.ArtworkImportStatus="missing"; missing++; }
                        else
                        {
                            var result=await download.DownloadAsync(printing.ExternalArtworkUrl,printing.ArtworkAssetId is null ? null : printing.ArtworkETag,printing.ArtworkAssetId is null ? null : printing.ArtworkLastModified,ct);
                            if(result.Status=="not_modified" && printing.ArtworkAssetId is null) throw new InvalidDataException("304 without stored artwork.");
                            if(result.Status=="not_modified" || result.Status=="ready" && result.SourceSha256==printing.ArtworkSha256 && printing.ArtworkAssetId is not null)
                            { printing.ArtworkImportStatus="ready"; unchanged++; }
                            else if(result.Status=="missing") { printing.ArtworkImportStatus="missing"; missing++; }
                            else
                            {
                                var imageInfo=Image.Identify(result.Image!); var thumbInfo=Image.Identify(result.Thumbnail!);
                                printing.ArtworkAssetId=await assets.StoreArtworkAsync(result.Image!,imageInfo.Width,imageInfo.Height,printing.ExternalArtworkUrl,false,ct);
                                printing.ThumbnailAssetId=await assets.StoreArtworkAsync(result.Thumbnail!,thumbInfo.Width,thumbInfo.Height,printing.ExternalArtworkUrl,true,ct);
                                printing.ArtworkSha256=result.SourceSha256; printing.ArtworkImportStatus="ready"; ready++;
                            }
                            if(result.Status=="ready") { printing.ArtworkETag=result.ETag; printing.ArtworkLastModified=result.LastModified; }
                        }
                        printing.ArtworkImportError=null;
                        if(printing.SourcePricingJson is not null || printing.Variants.Any(x=>x.RawValue?.TrimStart().StartsWith('{')==true))
                        {
                            var details=await catalog.GetPrinting(printing.Id,ct);
                            var quotes=CatalogPriceParser.Read(printing.SourcePricingJson,printing.Variants.ToArray(),rates);
                            if(quotes.Count>0)
                            {
                                var day=MarketPriceDay.At(clock.UtcNow);
                                var snapshot=await db.DailyMarketSnapshots.SingleOrDefaultAsync(x=>x.PrintingId==printing.Id && x.MarketDay==day.Date,ct);
                                if(snapshot is null) { snapshot=new() { PrintingId=printing.Id,MarketDay=day.Date }; db.DailyMarketSnapshots.Add(snapshot); }
                                snapshot.FetchedAt=clock.UtcNow; snapshot.RefreshAfter=day.RefreshAfter;
                                snapshot.Payload=JsonSerializer.Serialize(new ScannerCardDetailsDto(details!,new Dictionary<string,string>(),quotes,"Referência internacional convertida pela PTAX, importada para o catálogo local.",clock.UtcNow,day.RefreshAfter)); priced++;
                            }
                        }
                    }
                    catch(Exception error) when(error is not OperationCanceledException)
                    { printing.ArtworkImportStatus="failed"; printing.ArtworkImportError=error.GetType().Name; failed++; logger.LogWarning("Catalog artifact failed for {PrintingId}: {ErrorType}",printing.Id,error.GetType().Name); }
                    if(!recent) printing.ArtworkCheckedAt=clock.UtcNow; await db.SaveChangesAsync(ct);
                }
                after=page[^1].Id; db.ChangeTracker.Clear();
            }
            return new(ready,unchanged,missing,failed,priced);
        }
        finally
        {
            await using var release=connection.CreateCommand(); release.CommandText="SELECT pg_advisory_unlock(@key)";
            var argument=release.CreateParameter(); argument.ParameterName="key"; argument.Value=key; release.Parameters.Add(argument);
            try { await release.ExecuteNonQueryAsync(CancellationToken.None); } finally { await db.Database.CloseConnectionAsync(); }
        }
    }
}
