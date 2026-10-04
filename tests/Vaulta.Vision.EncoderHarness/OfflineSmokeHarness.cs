using System.Diagnostics;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vaulta.Vision.Encoding;
namespace Vaulta.Vision.EncoderHarness;

internal static class OfflineSmokeHarness
{
    public static async Task RunAsync(string[] paths)
    {
        foreach(var path in paths)
        {
            var source=EncoderManifest.Load(path);
            foreach(var mode in new[]{"center_crop","letterbox","direct_resize"})
            {
                var experiment=source.ForOffline(mode);var temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"smoke-"+Guid.NewGuid().ToString("N")+".json");
                try
                {
                    await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(experiment,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                    var watch=Stopwatch.StartNew();using var encoder=new OnnxImageEncoder(temp,1,true);var cold=watch.Elapsed.TotalMilliseconds;
                    using var image=new Image<Rgb24>(315,440);
                    for(var y=0;y<440;y++)for(var x=0;x<315;x++)image[x,y]=new((byte)(x%256),(byte)(y%256),(byte)((x+y)%256));
                    using var bytes=new MemoryStream();await image.SaveAsPngAsync(bytes);bytes.Position=0;watch.Restart();var result=await encoder.EncodeAsync(bytes,default);watch.Stop();
                    if(result.Vector.Length!=experiment.Dimension || result.Vector.Any(x=>!float.IsFinite(x)) || Math.Abs(result.Vector.Sum(x=>(double)x*x)-1)>.00001)throw new InvalidDataException("Real ONNX runtime returned an invalid embedding.");
                    Console.WriteLine(JsonSerializer.Serialize(new{model=experiment.ModelId,mode,dimension=result.Vector.Length,coldStartMs=cold,encodeMs=watch.Elapsed.TotalMilliseconds,weightsBytes=new FileInfo(Path.Combine(Path.GetDirectoryName(temp)!,experiment.ModelFile)).Length,kind="real-runtime-synthetic-input-smoke-not-phone-accuracy"}));
                }
                finally {File.Delete(temp);}
            }
        }
    }
}
