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
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var version)
                || !version.TryGetInt32(out var schema) || schema is not (1 or 2 or 3)) return null;
            var expected = schema switch
            {
                3 => [.. RootFields, "hp", "finish", "condition"],
                2 => [.. RootFields, "hp"],
                _ => RootFields
            };
            if (!HasExactProperties(root, expected)) return null;
            var game = ReadField(root, "gameCode", 32);
            var name = ReadField(root, "name", 160);
            var number = ReadField(root, "collectorNumber", 32);
            var setCode = ReadField(root, "setCode", 40);
            var setName = ReadField(root, "setName", 160);
            var language = ReadField(root, "language", 12);
            var variant = ReadField(root, "variant", 32);
            var hp = schema >= 2 ? ReadField(root, "hp", 4) : new CardEvidenceField(null, 0);
            var finish = schema >= 3 ? ReadField(root, "finish", 16) : new CardEvidenceField(null, 0);
            var condition = schema >= 3 ? ReadField(root, "condition", 24) : new CardEvidenceField(null, 0);
            if (game?.Value is not (null or "pokemon")
                || number?.Value is { } n && !Regex.IsMatch(n, @"^(?:[A-Z]{0,4}\s*)?\d{1,4}[A-Z]{0,2}(?:\s*/\s*(?:[A-Z]{0,4}\s*)?\d{1,4}[A-Z]{0,2})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || setCode?.Value is { } s && !Regex.IsMatch(s, @"^[a-zA-Z0-9][a-zA-Z0-9-]{0,39}$", RegexOptions.CultureInvariant)
                || language?.Value is { } l && !Regex.IsMatch(l, @"^[a-z]{2}(?:-[A-Z]{2})?$", RegexOptions.CultureInvariant)
                || variant?.Value is not (null or "normal" or "holo" or "reverse")
                || finish?.Value is not (null or "normal" or "holo" or "reverse" or "textured" or "full-art")
                || condition?.Value is not (null or "MINT" or "NEAR_MINT" or "LIGHTLY_PLAYED" or "MODERATELY_PLAYED" or "HEAVILY_PLAYED" or "DAMAGED")
                || hp?.Value is { } h && (!int.TryParse(h, out var points) || points is < 1 or > 9999)) return null;
            if (game is null || name is null || number is null || setCode is null || setName is null || language is null || variant is null || hp is null || finish is null || condition is null) return null;
            return new(game, name, number, setCode, setName, language, variant, promptVersion, modelVersion, hp, finish, condition);
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
