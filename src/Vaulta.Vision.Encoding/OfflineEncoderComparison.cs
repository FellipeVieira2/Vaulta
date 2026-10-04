using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Encoding;

public sealed record OfflineVisionImage(string SampleId,Guid? PrintingId,string Path,string Sha256,string SplitGroup,
    string Kind,string? LabelSource=null,bool ImprovementConsent=false,string? Condition=null);
public sealed record OfflineVisionDataset(string Version,IReadOnlyList<OfflineVisionImage> References,IReadOnlyList<OfflineVisionImage> Queries);

/// <summary>Explicit local experiment. No database, asset downloads, price lookup or production index writes.</summary>
public static class OfflineEncoderComparison
{
    public static IReadOnlyList<OfflineVisionImage> HeldOutReferences(OfflineVisionDataset dataset)
    {
        if(string.IsNullOrWhiteSpace(dataset.Version) || dataset.References.Count>250000 || dataset.Queries.Count>10000)throw new InvalidDataException("Invalid offline dataset scope.");
        foreach(var image in dataset.References.Concat(dataset.Queries))
            if(string.IsNullOrWhiteSpace(image.SampleId) || string.IsNullOrWhiteSpace(image.SplitGroup) || image.Sha256.Length!=64 || !image.Sha256.All(Uri.IsHexDigit)
                || image.PrintingId==Guid.Empty || image.Kind is not ("official-reference" or "verified-phone"))throw new InvalidDataException("Invalid image identity or split.");
        foreach(var query in dataset.Queries)
            if(query.Kind!="verified-phone" || query.LabelSource is not ("UserConfirmation" or "UserCorrection") || !query.ImprovementConsent)throw new InvalidDataException("Real-photo queries require explicit human labels and improvement consent.");
        var all=dataset.References.Concat(dataset.Queries).ToArray();
        var groupIds=all.Select(x=>x.SplitGroup).Distinct(StringComparer.Ordinal).ToDictionary(x=>x,x=>new Guid(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(x)).AsSpan(0,16)),StringComparer.Ordinal);
        (Guid Attempt,string Sha) Key(OfflineVisionImage image)=>(groupIds[image.SplitGroup],image.Sha256.ToLowerInvariant());
        // Shared hashes connect whole sessions, including transitive duplicates.
        var connected=VisionBenchmarkGrouping.Assign(all.Select(Key).ToArray());
        var excluded=dataset.Queries.Select(x=>connected[Key(x)]).ToHashSet(StringComparer.Ordinal);
        return dataset.References.Where(x=>x.PrintingId.HasValue && !excluded.Contains(connected[Key(x)])
            && (x.Kind=="official-reference" || x.LabelSource is "UserConfirmation" or "UserCorrection" && x.ImprovementConsent)).ToArray();
    }

    public static async Task RunAsync(string datasetPath,IReadOnlyList<string> manifestPaths,string outputPath,CancellationToken ct=default)
    {
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
        var dataset=JsonSerializer.Deserialize<OfflineVisionDataset>(await File.ReadAllTextAsync(datasetPath,ct),json)??throw new InvalidDataException("Empty offline dataset.");
        var references=HeldOutReferences(dataset);var reports=new List<object>();
        var root=Path.GetDirectoryName(Path.GetFullPath(datasetPath))!;
        if(dataset.Queries.Count>0)
        {
            if(references.Count==0)throw new InvalidDataException("No independent reference images remain after held-out exclusion.");
            foreach(var manifestPath in manifestPaths)
            {
                var source=EncoderManifest.Load(manifestPath);
                foreach(var mode in new[]{"center_crop","letterbox","direct_resize"})
                {
                    ct.ThrowIfCancellationRequested();
                    var manifest=source.ForOffline(mode);
                    // A temporary manifest beside the checksum-verified weights; never replaces the installed manifest.
                    var temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!,"offline-"+Guid.NewGuid().ToString("N")+".json");
                    try
                    {
                        await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(manifest,json),ct);
                        var startup=Stopwatch.StartNew();using var encoder=new OnnxImageEncoder(temp,1,offlineExperiment:true);startup.Stop();
                        var index=new CosineReferenceIndex(encoder.Identity);var staging=index.BeginSnapshot(encoder.Identity);
                        foreach(var image in references)
                        {
                            await using var input=VerifiedImage(root,image);var encoded=await encoder.EncodeAsync(input,ct);
                            var referenceId=new Guid(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(image.SampleId+":"+image.PrintingId+":"+image.Sha256)).AsSpan(0,16));
                            staging.Add(new(referenceId,image.PrintingId!.Value,encoded.Vector,image.Kind=="official-reference"?"official":"verified_capture"));
                        }
                        staging.Publish("offline-"+dataset.Version+"-"+mode);
                        var observations=new List<VisionRetrievalObservation>();var timings=new List<double>();
                        foreach(var image in dataset.Queries)
                        {
                            await using var input=VerifiedImage(root,image);var timer=Stopwatch.StartNew();var embedding=await encoder.EncodeAsync(input,ct);
                            var ranked=await index.SearchAsync(embedding,10,ct);timer.Stop();timings.Add(timer.Elapsed.TotalMilliseconds);
                            observations.Add(new(image.SampleId,image.PrintingId,ranked.Select(x=>x.PrintingId).ToArray(),null,"encoder_only",false,false,false,false));
                        }
                        var metrics=VisionRetrievalMetrics.Evaluate(observations);
                        var ordered=timings.Order().ToArray();double Percentile(double p)=>ordered[Math.Clamp((int)Math.Ceiling(p*ordered.Length)-1,0,ordered.Length-1)];
                        using var process=Process.GetCurrentProcess();
                        reports.Add(new{encoder=encoder.Identity,preprocessing=mode,referenceCount=references.Count,queryCount=dataset.Queries.Count,
                            metrics.Top1,metrics.Top5,metrics.Top10,metrics.Mrr,finalPrintingAccuracy=(object?)null,finalVariantAccuracy=(object?)null,
                            coldStartMs=startup.Elapsed.TotalMilliseconds,embeddingAndRetrievalP50Ms=Percentile(.5),embeddingAndRetrievalP95Ms=Percentile(.95),
                            managedMemoryBytes=GC.GetTotalMemory(false),workingSetBytes=process.WorkingSet64,peakWorkingSetBytes=process.PeakWorkingSet64,
                            weightsBytes=new FileInfo(Path.Combine(Path.GetDirectoryName(temp)!,manifest.ModelFile)).Length,
                            memoryScope="process-includes-host-and-previous-experiments",physicalAndroidDeviceTested=false});
                    }
                    finally{File.Delete(temp);}
                }
            }
        }
        await File.WriteAllTextAsync(Path.GetFullPath(outputPath),JsonSerializer.Serialize(new{dataset.Version,
            measurement=dataset.Queries.Count==0?"insufficient_real_world_dataset":"verified_phone_retrieval_only",
            dataset.Queries.Count,referenceCount=references.Count,excludedReferences=dataset.References.Count-references.Count,reports,
            productionChanged=false,onlineTraining=false},json),ct);
    }

    private static FileStream VerifiedImage(string root,OfflineVisionImage image)
    {
        var path=Path.GetFullPath(image.Path,root);
        if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Offline images must stay inside the frozen dataset directory.");
        var stream=File.OpenRead(path);
        try
        {
            if(stream.Length is 0 or >15*1024*1024 || !Convert.ToHexString(SHA256.HashData(stream)).Equals(image.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Frozen image checksum mismatch.");
            stream.Position=0;return stream;
        }
        catch{stream.Dispose();throw;}
    }
}
