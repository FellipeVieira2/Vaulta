using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vaulta.Vision.Contracts;
namespace Vaulta.Vision.Encoding;
public sealed record EncoderManifest(string ModelId,string Revision,string Sha256,string ModelFile,string Runtime,string InputTensor,string OutputTensor,
    int Dimension,string Normalization,string Pooling,string PreprocessingVersion,JsonElement Preprocessing)
{
    public const string SupportedPreprocessing="vaulta-imagesharp-bicubic-v1";
    public EncoderIdentity Identity=>new(ModelId,Revision,Sha256,PreprocessingVersion,Dimension,Runtime,InputTensor,OutputTensor,
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Preprocessing)))).ToLowerInvariant());
    public void Validate()
    {
        if(ModelId!="Xenova/clip-vit-base-patch32" || Revision.Length!=40 || !Revision.All(Uri.IsHexDigit)
            || Sha256.Length!=64 || !Sha256.All(Uri.IsHexDigit) || Path.GetFileName(ModelFile)!=ModelFile || !ModelFile.EndsWith(".onnx",StringComparison.Ordinal)
            || Dimension!=512 || Runtime!="onnxruntime-1.23.2" || InputTensor!="pixel_values" || OutputTensor!="image_embeds"
            || Pooling!="pooled" || Normalization!="l2" || PreprocessingVersion!=SupportedPreprocessing)
            throw new InvalidDataException("Unsupported or unsafe encoder manifest.");
        try
        {
            if(Preprocessing.GetProperty("crop_size").GetProperty("height").GetInt32()!=224 || Preprocessing.GetProperty("crop_size").GetProperty("width").GetInt32()!=224
                || Preprocessing.GetProperty("size").GetProperty("shortest_edge").GetInt32()!=224 || Preprocessing.GetProperty("resample").GetInt32()!=3
                || !Preprocessing.GetProperty("do_center_crop").GetBoolean() || !Preprocessing.GetProperty("do_convert_rgb").GetBoolean()
                || !Preprocessing.GetProperty("do_normalize").GetBoolean() || !Preprocessing.GetProperty("do_rescale").GetBoolean() || !Preprocessing.GetProperty("do_resize").GetBoolean()
                || Math.Abs(Preprocessing.GetProperty("rescale_factor").GetDouble()-1d/255d)>1e-12)
                throw new InvalidDataException("Unsupported image preprocessing.");
            var mean=Preprocessing.GetProperty("image_mean").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            var std=Preprocessing.GetProperty("image_std").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            if(mean.Length!=3 || std.Length!=3 || mean.Any(x=>!float.IsFinite(x)) || std.Any(x=>!float.IsFinite(x) || x<=0)) throw new InvalidDataException("Invalid RGB normalization.");
        }
        catch(Exception error) when(error is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Invalid image preprocessing manifest.",error); }
    }
    public static EncoderManifest Load(string path)=>JsonSerializer.Deserialize<EncoderManifest>(File.ReadAllText(path),new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Empty encoder manifest.");
}
