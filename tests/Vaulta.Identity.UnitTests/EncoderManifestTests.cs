using System.Text.Json;
using Vaulta.Vision.Encoding;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public sealed class EncoderManifestTests
{
    private static EncoderManifest Valid()
    {
        using var source=typeof(EncoderManifest).Assembly.GetManifestResourceStream("Vaulta.Vision.Encoding.Models.clip-base.json")!;
        return JsonSerializer.Deserialize<EncoderManifest>(source,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    [Fact]
    public void RejectsManifestWhichCouldLoadUnknownWeightsOrWrongTensorContract()
    {
        var model=Valid(); model.Validate();
        Assert.Throws<InvalidDataException>(()=>(model with { ModelFile="../other.onnx" }).Validate());
        Assert.Throws<InvalidDataException>(()=>(model with { Dimension=384 }).Validate());
        Assert.Throws<InvalidDataException>(()=>(model with { Sha256="not-a-hash" }).Validate());
        Assert.Throws<InvalidDataException>(()=>(model with { OutputTensor="last_hidden_state" }).Validate());
        Assert.Throws<InvalidDataException>(()=>(model with { PreprocessingVersion="hf-pillow-bicubic-v1" }).Validate());
    }
    [Fact]
    public void PreprocessingParametersArePartOfCompatibilityIdentity()
    {
        var model=Valid(); using var changed=JsonDocument.Parse("{\"image_mean\":[0.4,0.4,0.4]}");
        Assert.NotEqual(model.Identity,(model with { Preprocessing=changed.RootElement.Clone() }).Identity);
    }
}
