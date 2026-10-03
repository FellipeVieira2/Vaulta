using System.Text.Json;
using Vaulta.Vision.Infrastructure;
using Vaulta.Vision.Application;
using Vaulta.Vision.Domain;
using Vaulta.Assets.Application;
using Microsoft.EntityFrameworkCore;
namespace Vaulta.Web.Api;
internal static class VisionDatasetCommands
{
 public static async Task<bool> TryExecute(WebApplication app,string[] args)
 {
  var command=args.FirstOrDefault(x=>x is "--vision-review" or "--vision-export-manifest" or "--vision-purge-expired" or "--vision-rerun-benchmark");if(command is null)return false;
  await using var scope=app.Services.CreateAsyncScope();var service=scope.ServiceProvider.GetRequiredService<VisionHistoryService>();var i=Array.IndexOf(args,command);
  try
  {
   if(command=="--vision-review")
   {if(args.Length<=i+2 || !Guid.TryParse(args[i+1],out var attempt) || !Guid.TryParse(args[i+2],out var feedback))throw new ArgumentException("Usage: --vision-review <attemptId> <feedbackId>");Console.WriteLine(JsonSerializer.Serialize(new{sampleId=await service.PromoteAsync(attempt,feedback,CancellationToken.None)}));}
   else if(command=="--vision-export-manifest")
   {if(args.Length<=i+2)throw new ArgumentException("Usage: --vision-export-manifest <version> <outputPath>");var manifest=await service.ExportAsync(args[i+1],CancellationToken.None);await File.WriteAllTextAsync(Path.GetFullPath(args[i+2]),JsonSerializer.Serialize(manifest,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{version=manifest.DatasetVersion,samples=manifest.Samples.Count}));}
   else if(command=="--vision-rerun-benchmark")
   {
    if(args.Length<=i+3 || !int.TryParse(args[i+2],out var limit) || limit is <1 or >100)throw new ArgumentException("Usage: --vision-rerun-benchmark <version> <maxSamples1-100> <outputPath>");
    var manifest=await service.ExportAsync(args[i+1],CancellationToken.None);var excluded=await service.EvaluationExcludedAssetsAsync(manifest,CancellationToken.None);await scope.ServiceProvider.GetRequiredService<IVisualReferenceBuilder>().LoadForEvaluationAsync(excluded,CancellationToken.None);
    var db=scope.ServiceProvider.GetRequiredService<VisionDbContext>();var assets=scope.ServiceProvider.GetRequiredService<IPrivateAssetService>();var scanner=scope.ServiceProvider.GetRequiredService<ScannerVisionCoordinator>();var expected=new List<VisionBenchmarkLabel>();var predicted=new List<VisionBenchmarkLabel>();
    foreach(var sample in manifest.Samples.Take(limit))
    {
     var capture=await db.ScanCaptures.AsNoTracking().SingleAsync(x=>x.Id==sample.CaptureId);var attempt=await db.ScanAttempts.AsNoTracking().SingleAsync(x=>x.Id==capture.AttemptId);
     var image=await assets.ReadOwnedAsync(attempt.OwnerId,capture.AssetId,CancellationToken.None)??throw new InvalidDataException("Benchmark image is unavailable.");
     var result=await scanner.IdentifyAsync(attempt.OwnerId,image,null,attempt.Id,"benchmark-"+Guid.NewGuid().ToString("N"),CancellationToken.None);
     string? Field(string name)=>result.Evidence.TryGetValue(name,out var f) && double.IsFinite(f.Confidence) && f.Confidence is >=.8 and <=1?f.Value:null;
     expected.Add(new(sample.SampleId.ToString(),sample.PrintingId,sample.VariantId,sample.Orientation,sample.Presence));
     predicted.Add(new(sample.SampleId.ToString(),result.PrintingId,result.VariantId,Field("cardSide"),Field("isCard") switch{"true"=>"card-present","false"=>"no-card",_=>null}));
    }
    var report=new{datasetVersion=manifest.DatasetVersion,index=scope.ServiceProvider.GetRequiredService<IVisualReferenceIndex>().Status,predictions=predicted,metrics=VisionBenchmarkMetrics.Evaluate(expected,predicted)};
    await File.WriteAllTextAsync(Path.GetFullPath(args[i+3]),JsonSerializer.Serialize(report,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{version=manifest.DatasetVersion,samples=predicted.Count}));
   }
   else Console.WriteLine(JsonSerializer.Serialize(new{deleted=await service.PurgeExpiredAsync(CancellationToken.None)}));
  }
  catch(Exception e){app.Logger.LogError("Vision dataset command failed: {ErrorType}",e.GetType().Name);Environment.ExitCode=1;}return true;
 }
}
