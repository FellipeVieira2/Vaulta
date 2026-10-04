using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Xunit;

namespace Vaulta.Identity.UnitTests.Vision;

public sealed class CatalogVisionPreparationTests
{
    private static EncoderIdentity TestIdentity => new("test-model", "r1", "abc123", "v1", 512, "onnx", "input", "output");

    [Fact]
    public void Constructor_Does_Not_Require_Artwork_Importer()
    {
        var constructor = typeof(CatalogVisionPreparation).GetConstructors().Single();
        var parameters = constructor.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(IVisualReferenceBuilder), parameters[0].ParameterType);
    }

    [Fact]
    public void Report_Complete_Delegates_To_References()
    {
        var index = new VisualIndexStatus("v1", 100, TestIdentity);
        var references = new VisualReferenceBuildReport(10, 5, 0, 0, index);
        var report = new CatalogVisionPreparationReport(references);
        Assert.True(report.Complete);
        Assert.Equal(10, report.References.Generated);
    }

    [Fact]
    public void Report_Incomplete_When_Pending_Exist()
    {
        var index = new VisualIndexStatus("v1", 95, TestIdentity);
        var references = new VisualReferenceBuildReport(10, 5, 2, 0, index);
        var report = new CatalogVisionPreparationReport(references);
        Assert.False(report.Complete);
    }
}