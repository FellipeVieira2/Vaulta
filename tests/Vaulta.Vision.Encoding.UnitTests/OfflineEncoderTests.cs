using System.Text.Json;
using System.Text.Json.Nodes;
using Vaulta.Vision.Encoding;
using Xunit;

namespace Vaulta.Vision.Encoding.UnitTests;

public sealed class OfflineEncoderTests
{
    internal static EncoderManifest Baseline()
    {
        using var stream = typeof(EncoderManifest).Assembly.GetManifestResourceStream("Vaulta.Vision.Encoding.Models.clip-base.json")!;
        return JsonSerializer.Deserialize<EncoderManifest>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [Fact]
    public void ProductionRejectsDifferentWeightsEvenWhenChecksumHasValidFormat()
    {
        Assert.Throws<InvalidDataException>(() => (Baseline() with { Sha256 = new string('a', 64) }).Validate());
    }

    [Fact]
    public void ProductionRejectsDifferentPinnedRevision()
    {
        Assert.Throws<InvalidDataException>(() => (Baseline() with { Revision = new string('a', 40) }).Validate());
    }

    [Fact]
    public void ProductionRejectsNormalizationExperimentWithBaselineVersion()
    {
        var manifest = Baseline();
        var settings = JsonNode.Parse(manifest.Preprocessing.GetRawText())!;
        settings["image_mean"] = new JsonArray(.4f, .4f, .4f);
        Assert.Throws<InvalidDataException>(() => (manifest with { Preprocessing = JsonSerializer.SerializeToElement(settings) }).Validate());
    }
    [Fact] public void PreprocessingExperimentsAreDistinctAndCannotEnterProduction()
    {
        var baseline=Baseline();var letter=baseline.ForOffline("letterbox");var direct=baseline.ForOffline("direct_resize");
        Assert.NotEqual(baseline.Identity,letter.Identity);Assert.NotEqual(letter.Identity,direct.Identity);
        Assert.Throws<InvalidDataException>(letter.Validate);Assert.Throws<InvalidDataException>(direct.Validate);
    }
    [Fact] public void HeldOutShaAndSessionGroupCannotLeakIntoOfflineReferences()
    {
        var p=Guid.NewGuid();var sha=new string('a',64);
        var query=new OfflineVisionImage("query",p,"q.jpg",sha,"phone-session","verified-phone","UserCorrection",true);
        OfflineVisionImage Reference(string id,string hash,string group)=>new(id,p,id+".jpg",hash,group,"official-reference");
        var d=new OfflineVisionDataset("v1",[Reference("same-sha",sha,"duplicate-source"),Reference("same-session",new string('b',64),"phone-session"),Reference("independent",new string('c',64),"independent-source")],[query]);
        Assert.Equal("independent",Assert.Single(OfflineEncoderComparison.HeldOutReferences(d)).SampleId);
    }
    [Fact] public void DuplicateShaExcludesTheEntireRelatedSessionTransitively()
    {
        var p=Guid.NewGuid();var a=new string('a',64);var b=new string('b',64);var c=new string('c',64);
        OfflineVisionImage Image(string id,string sha,string group)=>new(id,p,id+".jpg",sha,group,"verified-phone","UserConfirmation",true);
        var dataset=new OfflineVisionDataset("v1",[Image("duplicate",a,"session-b"),Image("other-photo",b,"session-b"),Image("linked-session",b,"session-c"),Image("another-related",c,"session-c")],[Image("heldout",a,"session-a")]);
        Assert.Empty(OfflineEncoderComparison.HeldOutReferences(dataset));
    }
    [Fact] public async Task EmptyDatasetReportsUnavailableAccuracyWithoutLoadingWeights()
    {
        var root=Path.Combine(Path.GetTempPath(),"vaulta-offline-test-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var input=Path.Combine(root,"dataset.json");var report=Path.Combine(root,"report.json");
            await File.WriteAllTextAsync(input,JsonSerializer.Serialize(new OfflineVisionDataset("v1",[],[]),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await OfflineEncoderComparison.RunAsync(input,["nonexistent-model.json"],report);
            using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(report));
            Assert.Equal("insufficient_real_world_dataset",doc.RootElement.GetProperty("measurement").GetString());Assert.Equal(0,doc.RootElement.GetProperty("reports").GetArrayLength());
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact] public void LetterboxRetainsTopAndBottomEvidenceWhichCenterCropRemoves()
    {
        using var original=new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(224,448);
        for(var y=0;y<448;y++)for(var x=0;x<224;x++)original[x,y]=y<40?new(255,0,0):y>=408?new(0,0,255):new(255,255,255);
        using var center=original.Clone();using var letter=original.Clone();using var direct=original.Clone();
        EncoderPreprocessing.Apply(center,Baseline());EncoderPreprocessing.Apply(letter,Baseline().ForOffline("letterbox"));EncoderPreprocessing.Apply(direct,Baseline().ForOffline("direct_resize"));
        Assert.Equal(new SixLabors.ImageSharp.PixelFormats.Rgb24(255,255,255),center[112,0]);
        Assert.Equal(new SixLabors.ImageSharp.PixelFormats.Rgb24(255,0,0),letter[112,0]);
        Assert.Equal(new SixLabors.ImageSharp.PixelFormats.Rgb24(0,0,255),letter[112,223]);
        Assert.Equal(new SixLabors.ImageSharp.PixelFormats.Rgb24(127,127,127),letter[0,112]);
        Assert.Equal(new SixLabors.ImageSharp.PixelFormats.Rgb24(255,255,255),direct[0,112]);
    }
}
