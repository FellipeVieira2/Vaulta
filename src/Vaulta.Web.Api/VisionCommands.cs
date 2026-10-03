using System.Text.Json;
using Vaulta.Vision.Application;
using Vaulta.Vision.Encoding;
namespace Vaulta.Web.Api;
// Finite operator commands; none of these downloads run inside scanner requests.
internal static class VisionCommands
{
    public static async Task<bool> TryInstallModelAsync(string[] args)
    {
        var position=Array.IndexOf(args,"--vision-model-install"); if(position<0) return false;
        if(args.Length<=position+1) throw new ArgumentException("Usage: --vision-model-install <manifestPath>");
        using var http=new HttpClient { Timeout=TimeSpan.FromMinutes(10) };
        var manifest=await EncoderModelInstaller.InstallAsync(args[position+1],http,CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(new { installed=true,manifestPath=Path.GetFullPath(args[position+1]),model=manifest.Identity },new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return true;
    }
    public static void Configure(IConfiguration configuration,string[] args)
    {
        var position=Array.IndexOf(args,"--vision-index-build");
        if(position<0) return;
        if(args.Length<=position+1) throw new ArgumentException("Usage: --vision-index-build <manifestPath>");
        configuration["Vision:ModelManifestPath"]=Path.GetFullPath(args[position+1]);
    }
    public static async Task<bool> TryExecute(WebApplication app,string[] args)
    {
        var command=args.FirstOrDefault(x=>x is "--vision-index-build" or "--vision-index-status" or "--vision-index-probe"); if(command is null) return false;
        await using var scope=app.Services.CreateAsyncScope(); using var cancellation=new CancellationTokenSource();
        ConsoleCancelEventHandler cancel=(_,e)=>{ e.Cancel=true; cancellation.Cancel(); }; Console.CancelKeyPress+=cancel;
        try
        {
            var builder=scope.ServiceProvider.GetRequiredService<IVisualReferenceBuilder>(); object result;
            if(command=="--vision-index-build")
            { var report=await builder.BuildAsync(null,cancellation.Token); result=report; if(!report.Complete) Environment.ExitCode=1; }
            else
            {
                var status=await builder.LoadAsync(cancellation.Token); result=status;
                if(command=="--vision-index-probe")
                {
                    var position=Array.IndexOf(args,command); if(args.Length<=position+1) throw new ArgumentException("Usage: --vision-index-probe <imagePath>");
                    await using var image=File.OpenRead(args[position+1]); var encoder=scope.ServiceProvider.GetRequiredService<IImageEncoder>();
                    var embedding=await encoder.EncodeAsync(image,cancellation.Token);
                    result=new { index=status,matches=await scope.ServiceProvider.GetRequiredService<IVisualReferenceIndex>().SearchAsync(embedding,5,cancellation.Token) };
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented=true }));
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { Environment.ExitCode=130; }
        catch(Exception error) { app.Logger.LogError("Vision command failed with {ErrorType}",error.GetType().Name); Environment.ExitCode=1; }
        finally { Console.CancelKeyPress-=cancel; }
        return true;
    }
}
