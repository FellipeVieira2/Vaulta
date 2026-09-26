using Vaulta.Assets.Domain;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class AssetRulesTests
{
    [Fact]
    public void RejectsUnsupportedContentType() =>
        Assert.Throws<DomainException>(() => AssetRules.ValidateUpload("collection-item", "application/pdf", 100, null));

    [Fact]
    public void RejectsOversizedAssets() =>
        Assert.Throws<DomainException>(() => AssetRules.ValidateUpload("collection-item", "image/jpeg", 15_000_001, null));

    [Fact]
    public void AcceptsSupportedImageAndSha256() =>
        AssetRules.ValidateUpload("collection-item", "image/jpeg", 1024, new string('a', 64));
}
