using System.Runtime.InteropServices;
using System.Text.Json;
using Vaulta.Vision.Application;
namespace Vaulta.Web.Api;
internal static class VisionFollowCommand
{
    public static async Task<bool> TryExecute(WebApplication app,string[] args)
    {
        var position=Array.IndexOf(args,"--vision-index-follow");if(position<0)return false;
        var batch=args.Length>position+2?int.Parse(args[position+2],System.Globalization.CultureInfo.InvariantCulture):100;
        if(batch is <1 or >500)throw new ArgumentOutOfRangeException(nameof(batch));
        using var cancel=new CancellationTokenSource();ConsoleCancelEventHandler handler=(_,e)=>{e.Cancel=true;cancel.Cancel();};Console.CancelKeyPress+=handler;
        using var terminate=OperatingSystem.IsLinux()?PosixSignalRegistration.Create(PosixSignal.SIGTERM,c=>{c.Cancel=true;cancel.Cancel();}):null;
        try
        {
            while(!cancel.IsCancellationRequested)
            {
                await using var scope=app.Services.CreateAsyncScope();
                var report=await scope.ServiceProvider.GetRequiredService<IVisualReferenceBuilder>().BuildReadyBatchAsync(batch,TimeSpan.FromMinutes(5),cancel.Token);
                Console.WriteLine(JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,mode="incremental-official",report.Generated,report.Reused,report.Failed,report.RemainingReady,report.Busy},new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                await Task.Delay(report.Generated+report.Reused==0?TimeSpan.FromSeconds(30):TimeSpan.FromMilliseconds(500),cancel.Token);
            }
        }
        catch(OperationCanceledException) when(cancel.IsCancellationRequested){}
        finally{Console.CancelKeyPress-=handler;}
        return true;
    }
}
