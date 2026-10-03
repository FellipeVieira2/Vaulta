using System.Security.Cryptography;
using System.Text.Json;
namespace Vaulta.Vision.Encoding;
// Finite setup operation. Scanner requests never download weights or call a model hub.
public static class EncoderModelInstaller
{
    public static async Task<EncoderManifest> InstallAsync(string manifestPath,HttpClient http,CancellationToken ct)
    {
        var path=Path.GetFullPath(manifestPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if(!File.Exists(path))
        {
            await using var template=typeof(EncoderManifest).Assembly.GetManifestResourceStream("Vaulta.Vision.Encoding.Models.clip-base.json")!;
            await using var output=File.Create(path); await template.CopyToAsync(output,ct);
        }
        var manifest=EncoderManifest.Load(path); manifest.Validate();
        var weights=Path.Combine(Path.GetDirectoryName(path)!,manifest.ModelFile);
        if(File.Exists(weights))
        {
            await using var existing=File.OpenRead(weights);
            if(Convert.ToHexString(await SHA256.HashDataAsync(existing,ct)).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase)) return manifest;
        }
        var uri=$"https://huggingface.co/{manifest.ModelId}/resolve/{manifest.Revision}/onnx/vision_model_quantized.onnx";
        using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct); response.EnsureSuccessStatusCode();
        await using var input=await response.Content.ReadAsStreamAsync(ct); var pending=weights+".partial";
        try
        {
            await using(var output=File.Create(pending))
            {
                var buffer=new byte[65536]; int read; long total=0;
                while((read=await input.ReadAsync(buffer,ct))>0)
                { total+=read; if(total>200*1024*1024) throw new InvalidDataException("Encoder download exceeds the byte limit."); await output.WriteAsync(buffer.AsMemory(0,read),ct); }
            }
            await using(var verify=File.OpenRead(pending))
                if(!Convert.ToHexString(await SHA256.HashDataAsync(verify,ct)).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Downloaded encoder checksum mismatch.");
            File.Move(pending,weights,true); return manifest;
        }
        finally { if(File.Exists(pending)) File.Delete(pending); }
    }
}
