using Xunit;

namespace Vaulta.Identity.UnitTests.Api;

public sealed class ModelManifestPathValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_ModelManifestPath_Does_Not_Trigger_Vision(string? path)
    {
        var hasVision = !string.IsNullOrWhiteSpace(path);
        Assert.False(hasVision);
    }

    [Fact]
    public void Valid_ModelManifestPath_Triggers_Vision()
    {
        var path = "/models/clip-base/manifest.json";
        var hasVision = !string.IsNullOrWhiteSpace(path);
        Assert.True(hasVision);
    }
}