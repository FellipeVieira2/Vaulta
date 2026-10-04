using Vaulta.Vision.Encoding;
namespace Vaulta.Web.Api;
internal static class VisionOfflineEncoderCommands
{
    public static async Task<bool> TryExecuteAsync(string[] args)
    {
        var at=Array.IndexOf(args,"--vision-compare-encoders");if(at<0)return false;
        try
        {
            if(args.Length<at+4)throw new ArgumentException("Usage: --vision-compare-encoders <dataset.json> <report.json> <clip-manifest> [dinov2-manifest]");
            await OfflineEncoderComparison.RunAsync(args[at+1],args.Skip(at+3).ToArray(),args[at+2]);
            Console.WriteLine("Offline encoder report saved. Production configuration unchanged.");
        }
        catch(Exception e){Console.Error.WriteLine("Offline comparison failed: "+e.GetType().Name);Environment.ExitCode=1;}
        return true;
    }
}
