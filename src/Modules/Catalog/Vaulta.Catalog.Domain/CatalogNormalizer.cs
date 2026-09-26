using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Vaulta.Catalog.Domain;

public static partial class CatalogNormalizer
{
    public static string NormalizeName(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    public static string NormalizeCollectorNumber(string value) => WhitespaceRegex().Replace(value.Trim().ToUpperInvariant(), "");

    public static string NormalizeLanguage(string value) => string.IsNullOrWhiteSpace(value) ? "und" : value.Trim().Replace('_', '-');

    public static string? NormalizeCode(string? value) => string.IsNullOrWhiteSpace(value) ? null : WhitespaceRegex().Replace(value.Trim().ToLowerInvariant(), "-");

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
