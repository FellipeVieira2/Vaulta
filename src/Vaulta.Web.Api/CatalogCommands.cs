using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Vision.Application;
using Vaulta.Vision.Infrastructure;

namespace Vaulta.Web.Api;

// Operator-only process entry point: requires shell/container access and the deployment's DB configuration.
internal static class CatalogCommands
{
    public static string[] HostArguments(string[] args)
    {
        var result = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] is "--vision-index-build" or "--vision-index-probe" or "--vision-model-install") index++;
            else if(args[index]=="--vision-index-follow") {index++;if(index+1<args.Length && int.TryParse(args[index+1],out _))index++;}
            else if(args[index]=="--vision-rerun-benchmark") index+=3;
            else if(args[index] is "--vision-review" or "--vision-export-manifest") index+=2;
            else if(args[index]=="--vision-purge-expired") { }
            else if (args[index] == "--vision-index-status") { }
            else if (args[index] == "--catalog-sync") index += 2;
            else if (args[index] == "--catalog-assets-import") index++;
            else if (args[index] == "--catalog-sync-run") index++;
            else if (args[index] != "--catalog-sync-runs") result.Add(args[index]);
        }
        return result.ToArray();
    }

    public static async Task<bool> TryExecute(WebApplication app, string[] args)
    {
        var command = args.FirstOrDefault(x => x is "--catalog-sync" or "--catalog-sync-runs" or "--catalog-sync-run" or "--catalog-assets-import");
        if (command is null) return false;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var index = Array.IndexOf(args, command);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            object result;
            if (command == "--catalog-assets-import")
            {
                if(args.Length<=index+1 || args[index+1]!="all" && !Guid.TryParse(args[index+1],out _)) throw new ArgumentException("Usage: --catalog-assets-import <all|canonicalSetId>");
                Guid? setId=args[index+1]=="all" ? null : Guid.Parse(args[index+1]);
                var artworkReport=await scope.ServiceProvider.GetRequiredService<ICatalogArtifactImporter>().ImportAsync(setId,cancellation.Token);
                if(!string.IsNullOrWhiteSpace(app.Services.GetRequiredService<VisionOptions>().ModelManifestPath))
                {
                    var visionReport=await scope.ServiceProvider.GetRequiredService<CatalogVisionPreparation>().PrepareAsync(setId,cancellation.Token);
                    result=new { artwork=artworkReport, vision=visionReport }; if(artworkReport.Failed>0 || !visionReport.Complete) Environment.ExitCode=1;
                }
                else
                {
                    result=artworkReport; if(artworkReport.Failed>0) Environment.ExitCode=1;
                }
            }
            else if (command == "--catalog-sync")
            {
                if (args.Length <= index + 2) throw new ArgumentException("Usage: --catalog-sync <provider> <all|setId|resume:runId>");
                var id = await scope.ServiceProvider.GetRequiredService<ICatalogSync>().Synchronize(args[index + 1], args[index + 2], cancellation.Token);
                result = await db.SyncRuns.AsNoTracking().SingleAsync(x => x.Id == id, cancellation.Token);
                var sync=(Vaulta.Catalog.Domain.CatalogSyncRun)result;
                if (sync.Status is "partial" or "failed") Environment.ExitCode = 1;
                // Vision preparation is now a separate phase (--vision-index-build).
                // Metadata sync no longer triggers artwork or vision implicitly.
            }
            else if (command == "--catalog-sync-run")
            {
                if (args.Length <= index + 1 || !Guid.TryParse(args[index + 1], out var id))
                    throw new ArgumentException("Usage: --catalog-sync-run <runId>");
                result = await db.SyncRuns.AsNoTracking().SingleAsync(x => x.Id == id, cancellation.Token);
            }
            else result = await db.SyncRuns.AsNoTracking().OrderByDescending(x => x.StartedAt).ThenBy(x => x.Id).Take(50).ToArrayAsync(cancellation.Token);
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Environment.ExitCode = 130; }
        catch (Exception exception)
        {
            app.Logger.LogError("Catalog command failed with {ErrorType}; inspect catalog.sync_runs for status", exception.GetType().Name);
            Environment.ExitCode = 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
        return true;
    }
}
