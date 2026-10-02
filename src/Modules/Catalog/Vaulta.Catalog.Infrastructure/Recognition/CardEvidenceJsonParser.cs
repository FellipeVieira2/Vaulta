using System.Text.Json;
using System.Text.RegularExpressions;
using Vaulta.Catalog.Application;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public static class CardEvidenceJsonParser
{
    private static readonly string[] RootFields = ["schemaVersion", "gameCode", "name", "collectorNumber", "setCode", "setName", "language", "variant"];

    public static CardEvidence? Parse(string json, string promptVersion, string modelVersion)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16_384) return null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (!HasExactProperties(root, RootFields)
                || !root.GetProperty("schemaVersion").TryGetInt32(out var schema) || schema != 1) return null;
            var game = ReadField(root, "gameCode", 32);
            var name = ReadField(root, "name", 160);
            var number = ReadField(root, "collectorNumber", 32);
            var setCode = ReadField(root, "setCode", 40);
            var setName = ReadField(root, "setName", 160);
            var language = ReadField(root, "language", 12);
            var variant = ReadField(root, "variant", 32);
            if (game?.Value is not (null or "pokemon")
                || number?.Value is { } n && !Regex.IsMatch(n, @"^(?:[A-Z]{0,4}\s*)?\d{1,4}[A-Z]{0,2}(?:\s*/\s*(?:[A-Z]{0,4}\s*)?\d{1,4}[A-Z]{0,2})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || setCode?.Value is { } s && !Regex.IsMatch(s, @"^[a-zA-Z0-9][a-zA-Z0-9-]{0,39}$", RegexOptions.CultureInvariant)
                || language?.Value is { } l && !Regex.IsMatch(l, @"^[a-z]{2}(?:-[A-Z]{2})?$", RegexOptions.CultureInvariant)
                || variant?.Value is not (null or "normal" or "holo" or "reverse")) return null;
            if (game is null || name is null || number is null || setCode is null || setName is null || language is null || variant is null) return null;
            return new(game, name, number, setCode, setName, language, variant, promptVersion, modelVersion);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static CardEvidenceField? ReadField(JsonElement root, string key, int maxLength)
    {
        var field = root.GetProperty(key);
        if (!HasExactProperties(field, ["value", "confidence"])) return null;
        var confidence = field.GetProperty("confidence");
        if (confidence.ValueKind != JsonValueKind.Number || !confidence.TryGetDouble(out var score)
            || !double.IsFinite(score) || score is < 0 or > 1) return null;
        var value = field.GetProperty("value");
        if (value.ValueKind == JsonValueKind.Null) return score == 0 ? new(null, 0) : null;
        if (value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength || text.Any(char.IsControl)) return null;
        return new(text.Trim(), score);
    }

    private static bool HasExactProperties(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var names = element.EnumerateObject().Select(x => x.Name).ToArray();
        return names.Length == expected.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Length
            && names.All(x => expected.Contains(x, StringComparer.Ordinal));
    }
}
