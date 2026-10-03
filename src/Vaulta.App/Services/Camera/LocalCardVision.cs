using Vaulta.App.Core.Catalog;
using Vaulta.Vision.Encoding;
namespace Vaulta.App.Services.Camera;
public static class LocalCardVision
{
    private static readonly Lazy<Task<(OnnxImageEncoder Encoder,OrientationPrototypes Prototypes)>> Runtime=new(Initialize);
    private static async Task<(OnnxImageEncoder,OrientationPrototypes)> Initialize()
    {
        var folder=Path.Combine(FileSystem.AppDataDirectory,"vision","clip-base-v1"); Directory.CreateDirectory(folder);
        foreach(var name in new[]{"model.onnx","manifest.json"})
        {
            var target=Path.Combine(folder,name);
            if(name=="model.onnx" && File.Exists(target)) continue;
            await using var input=await FileSystem.OpenAppPackageFileAsync("vision/"+name);
            var pending=target+".partial"; await using(var output=File.Create(pending)) await input.CopyToAsync(output);
            File.Move(pending,target,true);
        }
        using var prototypes=await FileSystem.OpenAppPackageFileAsync("vision/orientation-prototypes.json");
        using var reader=new StreamReader(prototypes); var json=await reader.ReadToEndAsync();
        return await Task.Run(()=> { var encoder=new OnnxImageEncoder(Path.Combine(folder,"manifest.json"),1); return (encoder,OrientationPrototypes.Load(json,encoder.Identity)); });
    }
    public static async Task WarmupAsync(CancellationToken ct)=>await Runtime.Value.WaitAsync(ct);
    public static async Task<ScannerFrameKind> AnalyzeAsync(byte[] jpeg,CancellationToken ct)
    {
        var runtime=await Runtime.Value.WaitAsync(ct); using var image=new MemoryStream(jpeg,false);
        var encoded=await runtime.Encoder.EncodeAsync(image,ct);
        return runtime.Prototypes.Classify(encoded) switch {"front"=>ScannerFrameKind.Front,"back"=>ScannerFrameKind.Back,"no-card"=>ScannerFrameKind.NoCard,_=>ScannerFrameKind.Unknown};
    }
}
