using Vaulta.Catalog.Domain;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class CatalogNormalizerTests
{
    [Theory]
    [InlineData("  Pikáchu  EX  ", "pikachu ex")]
    [InlineData("Mr. Mime", "mr mime")]
    public void NormalizeNameRemovesDiacriticsAndCollapsesPunctuation(string input, string expected) =>
        Assert.Equal(expected, CatalogNormalizer.NormalizeName(input));

    [Theory]
    [InlineData("  007 / 198 ", "007/198")]
    [InlineData("sv1a-001", "SV1A-001")]
    public void NormalizeCollectorNumberIsStable(string input, string expected) =>
        Assert.Equal(expected, CatalogNormalizer.NormalizeCollectorNumber(input));

    [Theory]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("en", "en")]
    public void NormalizeLanguageUsesCanonicalSeparator(string input, string expected) =>
        Assert.Equal(expected, CatalogNormalizer.NormalizeLanguage(input));
}
