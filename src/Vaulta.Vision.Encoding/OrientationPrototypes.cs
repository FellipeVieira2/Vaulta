using System.Text.Json;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Encoding;
/// <summary>Zero-shot CLIP orientation evidence, not calibrated confidence or a trained detector.</summary>
public sealed class OrientationPrototypes
{
    private readonly EncoderIdentity _identity;
    private readonly Dictionary<string,float[]> _labels;
    public OrientationPrototypes(EncoderIdentity identity,IReadOnlyDictionary<string,float[]> labels)
    {
        if(labels.Count!=3 || !new[]{"front","back","no-card"}.All(labels.ContainsKey)) throw new InvalidDataException("Invalid orientation labels.");
        _identity=identity; _labels=labels.ToDictionary(x=>x.Key,x=>EmbeddingMath.Normalize(x.Value,identity.Dimension));
    }
    public static OrientationPrototypes Load(string json,EncoderIdentity identity)
    {
        using var document=JsonDocument.Parse(json); var root=document.RootElement;
        if(root.GetProperty("modelId").GetString()!=identity.ModelId || root.GetProperty("revision").GetString()!=identity.Revision || root.GetProperty("dimension").GetInt32()!=identity.Dimension)
            throw new InvalidDataException("Orientation prototypes do not match the image encoder.");
        return new(identity,root.GetProperty("labels").EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.EnumerateArray().Select(v=>v.GetSingle()).ToArray()));
    }
    public string Classify(ImageEmbedding image)
    {
        if(image.Identity!=_identity) throw new InvalidDataException("Orientation embedding model mismatch.");
        var vector=EmbeddingMath.Normalize(image.Vector,_identity.Dimension);
        var scores=_labels.Select(label=>(Label:label.Key,Score:label.Value.Select((v,i)=>(double)v*vector[i]).Sum())).OrderByDescending(x=>x.Score).ToArray();
        if(scores[0].Score<.15 || scores[0].Score-scores[1].Score<.03) return "unknown";
        return scores[0].Label;
    }
}
