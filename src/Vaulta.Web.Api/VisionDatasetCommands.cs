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
  var command=args.FirstOrDefault(x=>x is "--vision-review" or "--vision-export-manifest" or "--vision-purge-expired" or "--vision-rerun-benchmark" or "--vision-improvement-status");if(command is null)return false;
  await using var scope=app.Services.CreateAsyncScope();var service=scope.ServiceProvider.GetRequiredService<VisionHistoryService>();var i=Array.IndexOf(args,command);
  try
  {
   if(command=="--vision-improvement-status")
   {
    IVisualReferenceIndex? index=null;
    if(!string.IsNullOrWhiteSpace(scope.ServiceProvider.GetRequiredService<VisionOptions>().ModelManifestPath))
    {await scope.ServiceProvider.GetRequiredService<IVisualReferenceBuilder>().LoadAsync(CancellationToken.None);index=scope.ServiceProvider.GetRequiredService<IVisualReferenceIndex>();}
    Console.WriteLine(JsonSerializer.Serialize(await service.ImprovementStatusAsync(index,CancellationToken.None),new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
   }
   else if(command=="--vision-review")
   {if(args.Length<=i+2 || !Guid.TryParse(args[i+1],out var attempt) || !Guid.TryParse(args[i+2],out var feedback))throw new ArgumentException("Usage: --vision-review <attemptId> <feedbackId>");Console.WriteLine(JsonSerializer.Serialize(new{sampleId=await service.PromoteAsync(attempt,feedback,CancellationToken.None)}));}
   else if(command=="--vision-export-manifest")
   {if(args.Length<=i+2)throw new ArgumentException("Usage: --vision-export-manifest <version> <outputPath>");var manifest=await service.ExportAsync(args[i+1],CancellationToken.None);await File.WriteAllTextAsync(Path.GetFullPath(args[i+2]),JsonSerializer.Serialize(manifest,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{version=manifest.DatasetVersion,samples=manifest.Samples.Count}));}
   else if(command=="--vision-rerun-benchmark")
   {
    if(args.Length<=i+3 || !int.TryParse(args[i+2],out var limit) || limit is <1 or >100)throw new ArgumentException("Usage: --vision-rerun-benchmark <version> <maxSamples1-100> <outputPath>");
    var manifest=await service.ExportAsync(args[i+1],CancellationToken.None);
    await VisionDatasetBenchmark.RunAsync(scope.ServiceProvider,manifest,limit,args[i+3]);
   }
   else Console.WriteLine(JsonSerializer.Serialize(new{deleted=await service.PurgeExpiredAsync(CancellationToken.None)}));
  }
  catch(Exception e){app.Logger.LogError("Vision dataset command failed: {ErrorType}",e.GetType().Name);Environment.ExitCode=1;}return true;
 }
}
