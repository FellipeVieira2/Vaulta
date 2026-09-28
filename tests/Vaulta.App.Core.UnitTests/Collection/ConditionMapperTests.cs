using Vaulta.App.Core.Collection;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Collection;

public sealed class ConditionMapperTests
{
    [Theory]
    [InlineData("Mint", "MINT")]
    [InlineData("Near Mint", "NEAR_MINT")]
    [InlineData("near mint", "NEAR_MINT")]
    [InlineData("Lightly Played", "LIGHTLY_PLAYED")]
    [InlineData("Moderately Played", "MODERATELY_PLAYED")]
    [InlineData("Heavily Played", "HEAVILY_PLAYED")]
    [InlineData("Damaged", "DAMAGED")]
    public void ToCanonicalCode_MapsKnownUiLabels(string label, string expectedCode) =>
        Assert.Equal(expectedCode, ConditionMapper.ToCanonicalCode(label));

    [Fact]
    public void ToCanonicalCode_UnknownLabel_ResolvesToUnknownInsteadOfArbitraryText() =>
        Assert.Equal("UNKNOWN", ConditionMapper.ToCanonicalCode("Some random text the user typed"));

    [Fact]
    public void ToUiLabel_RoundTripsAllCanonicalCodes()
    {
        foreach (var label in ConditionMapper.UiLabels)
        {
            var code = ConditionMapper.ToCanonicalCode(label);
            Assert.Equal(label, ConditionMapper.ToUiLabel(code));
        }
    }
}
