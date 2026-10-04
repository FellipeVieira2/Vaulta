using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vaulta.Vision.Contracts;
namespace Vaulta.Vision.Encoding;
public sealed record EncoderManifest(string ModelId,string Revision,string Sha256,string ModelFile,string Runtime,string InputTensor,string OutputTensor,
    int Dimension,string Normalization,string Pooling,string PreprocessingVersion,JsonElement Preprocessing)
{
    public const string SupportedPreprocessing="vaulta-imagesharp-bicubic-v1";
    public const string ClipRevision="d15189d7028b43f1d3e65039190477f6af591c2a";
    public const string ClipChecksum="583fd1110a514667812fee7d684952aaf82a99b959760c8d7dca7e0ab9839299";
    public const string DinoRevision="c2bb04a51fab207c420665f1946016107bffc701";
    public const string DinoChecksum="3afdc8bc63b50558d6e5770f5b799bb82455c2311183a2de43803f343a29d917";
    public const string LetterboxPreprocessing="vaulta-imagesharp-letterbox-v1";
    public const string DirectResizePreprocessing="vaulta-imagesharp-direct-resize-v1";
    public const string DinoCenterPreprocessing="vaulta-imagesharp-dinov2-center-crop-v1";
    public EncoderIdentity Identity=>new(ModelId,Revision,Sha256,PreprocessingVersion,Dimension,Runtime,InputTensor,OutputTensor,
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Preprocessing)))).ToLowerInvariant());
    public void Validate()
    {
        if(ModelId!="Xenova/clip-vit-base-patch32" || Revision!=ClipRevision || !Sha256.Equals(ClipChecksum,StringComparison.OrdinalIgnoreCase)
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
            RequireNormalization(mean,std,[.48145466f,.4578275f,.40821073f],[.26862954f,.26130258f,.27577711f]);
        }
        catch(Exception error) when(error is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Invalid image preprocessing manifest.",error); }
    }
    public EncoderManifest ForOffline(string mode)
    {
        var version=mode switch
        {"center_crop"=>ModelId=="Xenova/dinov2-small"?DinoCenterPreprocessing:SupportedPreprocessing,"letterbox"=>LetterboxPreprocessing,"direct_resize"=>DirectResizePreprocessing,_=>throw new InvalidDataException("Unknown preprocessing experiment.")};
        var experiment=this with{PreprocessingVersion=version};experiment.ValidateOffline();return experiment;
    }
    public void ValidateOffline()
    {
        if(PreprocessingVersion is not (SupportedPreprocessing or LetterboxPreprocessing or DirectResizePreprocessing or DinoCenterPreprocessing))throw new InvalidDataException("Unknown offline preprocessing identity.");
        if(ModelId=="Xenova/clip-vit-base-patch32")
        {if(PreprocessingVersion==DinoCenterPreprocessing)throw new InvalidDataException("Wrong model preprocessing.");(this with{PreprocessingVersion=SupportedPreprocessing}).Validate();return;}
        if(ModelId!="Xenova/dinov2-small" || Revision!=DinoRevision || !Sha256.Equals(DinoChecksum,StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(ModelFile)!=ModelFile || !ModelFile.EndsWith(".onnx",StringComparison.Ordinal) || Runtime!="onnxruntime-1.23.2"
            || InputTensor!="pixel_values" || OutputTensor!="last_hidden_state" || Dimension!=384 || Pooling!="cls" || Normalization!="l2"
            || PreprocessingVersion==SupportedPreprocessing)throw new InvalidDataException("Unsupported offline model contract.");
        try
        {
            if(Preprocessing.GetProperty("crop_size").GetProperty("height").GetInt32()!=224 || Preprocessing.GetProperty("crop_size").GetProperty("width").GetInt32()!=224
                || Preprocessing.GetProperty("size").GetProperty("shortest_edge").GetInt32()!=256 || Preprocessing.GetProperty("resample").GetInt32()!=3
                || !Preprocessing.GetProperty("do_center_crop").GetBoolean() || !Preprocessing.GetProperty("do_convert_rgb").GetBoolean()
                || !Preprocessing.GetProperty("do_normalize").GetBoolean() || !Preprocessing.GetProperty("do_rescale").GetBoolean() || !Preprocessing.GetProperty("do_resize").GetBoolean()
                || Math.Abs(Preprocessing.GetProperty("rescale_factor").GetDouble()-1d/255d)>1e-12)throw new InvalidDataException("Unsupported DINOv2 preprocessing.");
            RequireNormalization(Preprocessing.GetProperty("image_mean").EnumerateArray().Select(x=>x.GetSingle()).ToArray(),Preprocessing.GetProperty("image_std").EnumerateArray().Select(x=>x.GetSingle()).ToArray(),[.485f,.456f,.406f],[.229f,.224f,.225f]);
        }
        catch(Exception e) when(e is KeyNotFoundException or InvalidOperationException or FormatException){throw new InvalidDataException("Invalid offline preprocessing.",e);}
    }
    private static void RequireNormalization(float[] mean,float[] std,float[] expectedMean,float[] expectedStd)
    {if(mean.Length!=3 || std.Length!=3 || Enumerable.Range(0,3).Any(i=>!float.IsFinite(mean[i]) || !float.IsFinite(std[i]) || Math.Abs(mean[i]-expectedMean[i])>1e-7 || Math.Abs(std[i]-expectedStd[i])>1e-7))throw new InvalidDataException("Normalization differs from the pinned encoder.");}
    public static EncoderManifest Load(string path)=>JsonSerializer.Deserialize<EncoderManifest>(File.ReadAllText(path),new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Empty encoder manifest.");
}
