using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Encoding;
// Shared preprocessing/runtime contract. Android supplies its native ONNX runtime separately.
public sealed class OnnxImageEncoder : Vaulta.Vision.Application.IImageEncoder,IDisposable
{
    private readonly InferenceSession _session;
    private readonly SemaphoreSlim _slots;
    private readonly EncoderManifest _manifest;
    private readonly float[] _mean;
    private readonly float[] _std;
    public EncoderIdentity Identity=>_manifest.Identity;
    public OnnxImageEncoder(string manifestPath,int concurrency=2)
    {
        _manifest=EncoderManifest.Load(manifestPath); _manifest.Validate();
        if(concurrency is <1 or >4) throw new ArgumentOutOfRangeException(nameof(concurrency));
        var weights=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!,_manifest.ModelFile);
        using(var stream=File.OpenRead(weights))
            if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(_manifest.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Encoder weights checksum mismatch.");
        using var options=new SessionOptions { IntraOpNumThreads=2,InterOpNumThreads=1,GraphOptimizationLevel=GraphOptimizationLevel.ORT_ENABLE_ALL };
        _session=new InferenceSession(weights,options);
        if(!_session.InputMetadata.ContainsKey(_manifest.InputTensor) || !_session.OutputMetadata.TryGetValue(_manifest.OutputTensor,out var output)
            || output.Dimensions[^1]!=_manifest.Dimension)
        { _session.Dispose(); throw new InvalidDataException("Actual model tensors do not match the manifest."); }
        _slots=new(concurrency,concurrency);
        _mean=_manifest.Preprocessing.GetProperty("image_mean").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        _std=_manifest.Preprocessing.GetProperty("image_std").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
    }
    public async Task<ImageEmbedding> EncodeAsync(Stream image,CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        try
        {
            using var bytes=new MemoryStream(); var buffer=new byte[16384]; int read;
            while((read=await image.ReadAsync(buffer,ct))>0)
            { if(bytes.Length+read>15*1024*1024) throw new InvalidDataException("Capture exceeds the byte limit."); await bytes.WriteAsync(buffer.AsMemory(0,read),ct); }
            var input=bytes.ToArray();
            return await Task.Run(()=>Encode(input,ct),ct);
        }
        finally { _slots.Release(); }
    }
    private ImageEmbedding Encode(byte[] bytes,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info=Image.Identify(bytes);
        if(info.Width<1 || info.Height<1 || info.Width>8000 || info.Height>8000 || (long)info.Width*info.Height>20_000_000 || Math.Max(info.Width,info.Height)/(double)Math.Min(info.Width,info.Height)>8) throw new InvalidDataException("Capture exceeds the pixel limit.");
        using var image=Image.Load<Rgb24>(new DecoderOptions { MaxFrames=1 },bytes);
        image.Mutate(context=>context.AutoOrient());
        var scale=224d/Math.Min(image.Width,image.Height);
        var width=Math.Max(224,(int)(image.Width*scale)); var height=Math.Max(224,(int)(image.Height*scale));
        image.Mutate(context=>context.Resize(width,height,KnownResamplers.Bicubic).Crop(new Rectangle((width-224)/2,(height-224)/2,224,224)));
        const int plane=224*224; var pixels=new float[3*plane];
        image.ProcessPixelRows(accessor=>
        {
            for(var y=0;y<224;y++)
            {
                var row=accessor.GetRowSpan(y);
                for(var x=0;x<224;x++)
                {
                    var at=y*224+x; var rgb=row[x];
                    pixels[at]=(rgb.R/255f-_mean[0])/_std[0];
                    pixels[plane+at]=(rgb.G/255f-_mean[1])/_std[1];
                    pixels[2*plane+at]=(rgb.B/255f-_mean[2])/_std[2];
                }
            }
        });
        using var run=new RunOptions(); using var cancelled=ct.Register(()=>run.Terminate=true);
        var tensor=new DenseTensor<float>(pixels,[1,3,224,224]);
        using var results=_session.Run([NamedOnnxValue.CreateFromTensor(_manifest.InputTensor,tensor)],[_manifest.OutputTensor],run);
        ct.ThrowIfCancellationRequested();
        return new(Identity,EmbeddingMath.Normalize(results.Single().AsTensor<float>().ToArray(),_manifest.Dimension));
    }
    public void Dispose() { _session.Dispose(); _slots.Dispose(); }
}
