using Vaulta.Vision.Application;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Infrastructure;
public sealed partial class VisualReferenceBuilder
{
    public async Task<VisualReferenceBatchReport> BuildReadyBatchAsync(int batchSize,TimeSpan retryAfter,CancellationToken ct,Guid? setId=null)
    {
        if(batchSize is <1 or >500 || retryAfter<TimeSpan.FromSeconds(30) || retryAfter>TimeSpan.FromHours(24))throw new ArgumentOutOfRangeException(nameof(batchSize));
        var model=ModelVersion(encoder.Identity);var retryBefore=clock.UtcNow-retryAfter;
        // Same session advisory lock as full builds: two writers never infer/upsert
        // the same model concurrently. No transaction spans S3 reads or inference.
        await db.Database.OpenConnectionAsync(ct);var connection=db.Database.GetDbConnection();
        var key=BitConverter.ToInt64(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("vaulta:vision:index:"+model)),0);
        await using var acquire=connection.CreateCommand();acquire.CommandText="SELECT pg_try_advisory_lock(@key)";
        var parameter=acquire.CreateParameter();parameter.ParameterName="key";parameter.Value=key;acquire.Parameters.Add(parameter);
        var locked=false;
        try
        {
            locked=(bool)(await acquire.ExecuteScalarAsync(ct)??false);
            if(!locked)return new(0,0,0,null,true);
            IQueryable<Vaulta.Catalog.Domain.Printing> Ready(bool includeRecentFailures)=>catalog.Printings.FromSql($"SELECT p.* FROM catalog.printings p WHERE p.is_active AND p.artwork_asset_id IS NOT NULL AND p.artwork_import_status='ready' AND NOT EXISTS (SELECT 1 FROM vision.visual_references r WHERE r.printing_id=p.id AND r.source_asset_id=p.artwork_asset_id AND r.model_version={model} AND r.origin='official' AND r.status='ready' AND r.vector IS NOT NULL) AND ({includeRecentFailures} OR NOT EXISTS (SELECT 1 FROM vision.visual_references r WHERE r.printing_id=p.id AND r.source_asset_id=p.artwork_asset_id AND r.model_version={model} AND r.origin='official' AND r.status='failed' AND r.updated_at>{retryBefore}))")
                .AsNoTracking().Where(x=>setId==null || x.SetId==setId);
            var page=await Ready(false).OrderBy(x=>x.Id).Take(batchSize).Select(x=>new{x.Id,x.ArtworkAssetId,x.ArtworkSha256}).ToArrayAsync(ct);
            var printingIds=page.Select(x=>x.Id).ToArray();var assetIds=page.Select(x=>x.ArtworkAssetId!.Value).ToArray();
            var rows=await db.VisualReferences.Where(x=>x.ModelVersion==model && x.Origin=="official" && printingIds.Contains(x.PrintingId)).ToArrayAsync(ct);
            var reusable=(await db.VisualReferences.AsNoTracking().Where(x=>x.ModelVersion==model && x.Origin=="official" && x.Status=="ready" && x.Vector!=null && x.SourceAssetId!=null && assetIds.Contains(x.SourceAssetId.Value)).ToArrayAsync(ct)).GroupBy(x=>x.SourceAssetId!.Value).ToDictionary(x=>x.Key,x=>x.First());
            var generated=0;var reused=0;var failed=0;
            foreach(var printing in page)
            {
                ct.ThrowIfCancellationRequested();
                var row=rows.SingleOrDefault(x=>x.PrintingId==printing.Id && x.SourceAssetId==printing.ArtworkAssetId);
                if(row is null){row=new(){Id=Guid.NewGuid(),PrintingId=printing.Id,SourceAssetId=printing.ArtworkAssetId,ModelVersion=model,ModelManifestJson=JsonSerializer.Serialize(encoder.Identity),Dimension=encoder.Identity.Dimension,CreatedAt=clock.UtcNow};db.VisualReferences.Add(row);}
                row.Status="pending";row.UpdatedAt=clock.UtcNow;row.ErrorCategory=null;row.OriginalSourceSha256=printing.ArtworkSha256;await db.SaveChangesAsync(ct);
                try
                {
                    if(reusable.TryGetValue(printing.ArtworkAssetId!.Value,out var existing)){row.Vector=EmbeddingMath.Normalize(existing.Vector!,encoder.Identity.Dimension);row.ImageSha256=existing.ImageSha256;reused++;}
                    else
                    {
                        var bytes=await assets.ReadArtworkAsync(printing.ArtworkAssetId.Value,ct)??throw new FileNotFoundException("Ready artwork is unavailable.");
                        using var image=new MemoryStream(bytes,false);var result=await encoder.EncodeAsync(image,ct);
                        if(result.Identity!=encoder.Identity)throw new InvalidDataException("Encoder identity mismatch.");
                        row.Vector=EmbeddingMath.Normalize(result.Vector,encoder.Identity.Dimension);row.ImageSha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();generated++;
                    }
                    var vectorBytes=new byte[row.Vector.Length*sizeof(float)];Buffer.BlockCopy(row.Vector,0,vectorBytes,0,vectorBytes.Length);row.VectorSha256=Convert.ToHexString(SHA256.HashData(vectorBytes)).ToLowerInvariant();row.Status="ready";reusable[printing.ArtworkAssetId.Value]=row;
                }
                catch(Exception error) when(error is not OperationCanceledException){row.Status="failed";row.ErrorCategory=error.GetType().Name;row.Vector=null;failed++;}
                row.UpdatedAt=clock.UtcNow;await db.SaveChangesAsync(ct);
            }
            return new(generated,reused,failed,await Ready(true).CountAsync(ct));
        }
        finally
        {
            if(locked){await using var release=connection.CreateCommand();release.CommandText="SELECT pg_advisory_unlock(@key)";var p=release.CreateParameter();p.ParameterName="key";p.Value=key;release.Parameters.Add(p);await release.ExecuteNonQueryAsync(CancellationToken.None);}
            await db.Database.CloseConnectionAsync();
        }
    }
}
