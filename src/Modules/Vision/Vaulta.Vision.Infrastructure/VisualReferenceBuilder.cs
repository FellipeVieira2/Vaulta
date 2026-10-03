using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vaulta.Assets.Application;
using Vaulta.Catalog.Infrastructure;
using Vaulta.SharedKernel;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Infrastructure;
public sealed class VisualReferenceBuilder(VisionDbContext db,CatalogDbContext catalog,ISystemAssetService assets,IImageEncoder encoder,CosineReferenceIndex index,IClock clock) : IVisualReferenceBuilder
{
    public static string ModelVersion(EncoderIdentity identity)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity))).ToLowerInvariant();
    public async Task<VisualReferenceBuildReport> BuildAsync(Guid? setId,CancellationToken ct)
    {
        var model=ModelVersion(encoder.Identity); var manifest=JsonSerializer.Serialize(encoder.Identity);
        await db.Database.OpenConnectionAsync(ct); var connection=db.Database.GetDbConnection();
        var key=BitConverter.ToInt64(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("vaulta:vision:index:"+model)),0);
        await using var acquire=connection.CreateCommand(); acquire.CommandText="SELECT pg_try_advisory_lock(@key)";
        var parameter=acquire.CreateParameter(); parameter.ParameterName="key"; parameter.Value=key; acquire.Parameters.Add(parameter);
        var locked=(bool)(await acquire.ExecuteScalarAsync(ct) ?? false);
        if(!locked) { await db.Database.CloseConnectionAsync(); throw new InvalidOperationException("Visual reference build already running for this model."); }
        var generated=0; var unchanged=0; var pending=0; var failed=0;
        try
        {
            Guid? after=null;
            while(true)
            {
                var page=await catalog.Printings.AsNoTracking().Where(x=>x.IsActive && (setId==null || x.SetId==setId) && (after==null || x.Id.CompareTo(after.Value)>0))
                    .OrderBy(x=>x.Id).Take(100).Select(x=>new { x.Id,x.ArtworkAssetId,x.ArtworkSha256 }).ToArrayAsync(ct);
                if(page.Length==0) break;
                var printingIds=page.Select(x=>x.Id).ToArray(); var assetIds=page.Where(x=>x.ArtworkAssetId!=null).Select(x=>x.ArtworkAssetId!.Value).ToArray();
                var known=await db.VisualReferences.Where(x=>x.ModelVersion==model && x.Origin=="official" && printingIds.Contains(x.PrintingId)).ToArrayAsync(ct);
                var reusable=(await db.VisualReferences.AsNoTracking().Where(x=>x.ModelVersion==model && x.Origin=="official" && x.Status=="ready" && x.SourceAssetId!=null && assetIds.Contains(x.SourceAssetId.Value)).ToArrayAsync(ct))
                    .GroupBy(x=>x.SourceAssetId!.Value).ToDictionary(x=>x.Key,x=>x.First());
                foreach(var printing in page)
                {
                    ct.ThrowIfCancellationRequested();
                    var row=known.SingleOrDefault(x=>x.PrintingId==printing.Id && x.SourceAssetId==printing.ArtworkAssetId);
                    if(row?.Status=="ready" && row.Vector is not null) { unchanged++; continue; }
                    if(row is null)
                    {
                        row=new() { Id=Guid.NewGuid(),PrintingId=printing.Id,SourceAssetId=printing.ArtworkAssetId,ModelVersion=model,ModelManifestJson=manifest,Dimension=encoder.Identity.Dimension,CreatedAt=clock.UtcNow };
                        db.VisualReferences.Add(row);
                    }
                    row.UpdatedAt=clock.UtcNow; row.OriginalSourceSha256=printing.ArtworkSha256; row.ErrorCategory=null;
                    if(printing.ArtworkAssetId is null) { row.Status="pending"; pending++; await db.SaveChangesAsync(ct); continue; }
                    row.Status="pending"; await db.SaveChangesAsync(ct);
                    try
                    {
                        if(reusable.TryGetValue(printing.ArtworkAssetId.Value,out var existing))
                        { row.Vector=EmbeddingMath.Normalize(existing.Vector!,encoder.Identity.Dimension); row.ImageSha256=existing.ImageSha256; }
                        else
                        {
                            var bytes=await assets.ReadArtworkAsync(printing.ArtworkAssetId.Value,ct) ?? throw new FileNotFoundException("Owned artwork is unavailable.");
                            using var image=new MemoryStream(bytes,false); var result=await encoder.EncodeAsync(image,ct);
                            if(result.Identity!=encoder.Identity) throw new InvalidDataException("Encoder returned an incompatible model identity.");
                            row.Vector=EmbeddingMath.Normalize(result.Vector,encoder.Identity.Dimension); row.ImageSha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                        }
                        var vectorBytes=new byte[row.Vector!.Length*sizeof(float)]; Buffer.BlockCopy(row.Vector,0,vectorBytes,0,vectorBytes.Length);
                        row.VectorSha256=Convert.ToHexString(SHA256.HashData(vectorBytes)).ToLowerInvariant(); row.Status="ready"; generated++;
                        reusable[printing.ArtworkAssetId.Value]=row;
                    }
                    catch(Exception error) when(error is not OperationCanceledException)
                    { row.Status="failed"; row.ErrorCategory=error.GetType().Name; row.Vector=null; failed++; }
                    await db.SaveChangesAsync(ct);
                }
                after=page[^1].Id; db.ChangeTracker.Clear();
            }
            return new(generated,unchanged,pending,failed,await LoadAsync(ct));
        }
        finally
        {
            await using var release=connection.CreateCommand(); release.CommandText="SELECT pg_advisory_unlock(@key)";
            var parameter2=release.CreateParameter(); parameter2.ParameterName="key"; parameter2.Value=key; release.Parameters.Add(parameter2);
            try { await release.ExecuteNonQueryAsync(CancellationToken.None); } finally { await db.Database.CloseConnectionAsync(); }
        }
    }
    public Task<VisualIndexStatus> LoadAsync(CancellationToken ct)=>LoadCoreAsync([],ct);
    public Task<VisualIndexStatus> LoadForEvaluationAsync(IReadOnlyCollection<Guid> excludedAssetIds,CancellationToken ct)=>LoadCoreAsync(excludedAssetIds.ToArray(),ct);
    private async Task<VisualIndexStatus> LoadCoreAsync(Guid[] excludedAssetIds,CancellationToken ct)
    {
        var model=ModelVersion(encoder.Identity); var references=new List<VisualIndexEntry>(); var versions=new List<string>(); Guid? after=null;
        while(true)
        {
            var page=await db.VisualReferences.FromSql($"SELECT r.* FROM vision.visual_references r JOIN catalog.printings p ON p.id=r.printing_id WHERE p.is_active AND r.status='ready' AND ((r.origin='official' AND r.source_asset_id=p.artwork_asset_id) OR (r.origin='verified_capture' AND EXISTS (SELECT 1 FROM vision.scan_captures c JOIN vision.scan_attempts a ON a.id=c.attempt_id JOIN vision.reviewed_samples s ON s.capture_id=c.id JOIN vision.feedback f ON f.id=s.feedback_id WHERE c.asset_id=r.source_asset_id AND c.status='ready' AND a.deletion_requested_at IS NULL AND a.retention_until>now() AND a.improvement_policy_version IS NOT NULL AND f.printing_id=r.printing_id AND f.orientation='front' AND f.presence='card-present' AND NOT EXISTS (SELECT 1 FROM vision.feedback newer WHERE newer.run_id=f.run_id AND newer.created_at>f.created_at)))) AND r.model_version={model}")
                .AsNoTracking().Where(x=>after==null || x.Id.CompareTo(after.Value)>0).Where(x=>x.SourceAssetId==null || !excludedAssetIds.Contains(x.SourceAssetId.Value)).OrderBy(x=>x.Id).Take(1000).ToArrayAsync(ct);
            if(page.Length==0) break;
            if(references.Count+page.Length>index.MaxReferences) throw new InvalidOperationException("Visual index exceeds configured capacity.");
            foreach(var row in page)
            {
                var identity=JsonSerializer.Deserialize<EncoderIdentity>(row.ModelManifestJson);
                if(identity!=encoder.Identity || row.Dimension!=encoder.Identity.Dimension || row.Vector is null) throw new InvalidDataException("Persisted reference model contract mismatch.");
                references.Add(new(row.Id,row.PrintingId,row.Vector,row.Origin)); versions.Add($"{row.Id:N}:{row.VectorSha256}");
            }
            after=page[^1].Id;
        }
        var version=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(model+":"+string.Join(";",versions)))).ToLowerInvariant();
        index.Publish(encoder.Identity,version,references); return index.Status;
    }
}
