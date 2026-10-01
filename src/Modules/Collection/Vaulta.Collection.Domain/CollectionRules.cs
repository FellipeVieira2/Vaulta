using System.Text.RegularExpressions;
using Vaulta.SharedKernel;

namespace Vaulta.Collection.Domain;

public static partial class CollectionRules
{
    public const string ActiveStatus = "ACTIVE";
    public const string RemovedStatus = "REMOVED";
    public const string SoldStatus = "SOLD";

    /// <summary>
    /// The only condition codes persisted by Collection. Raw/free text from users or clients is
    /// normalized and mapped to one of these via <see cref="ConditionAliases"/>; anything that does
    /// not resolve to a known alias is rejected rather than stored as an arbitrary string.
    /// </summary>
    public static readonly IReadOnlyList<string> CanonicalConditions =
    [
        "MINT", "NEAR_MINT", "LIGHTLY_PLAYED", "MODERATELY_PLAYED", "HEAVILY_PLAYED", "DAMAGED", "UNKNOWN"
    ];

    private static readonly IReadOnlyDictionary<string, string> ConditionAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["M"] = "MINT",
        ["MINT"] = "MINT",
        ["NM"] = "NEAR_MINT",
        ["NEAR_MINT"] = "NEAR_MINT",
        ["LP"] = "LIGHTLY_PLAYED",
        ["LIGHTLY_PLAYED"] = "LIGHTLY_PLAYED",
        ["MP"] = "MODERATELY_PLAYED",
        ["MODERATELY_PLAYED"] = "MODERATELY_PLAYED",
        ["HP"] = "HEAVILY_PLAYED",
        ["HEAVILY_PLAYED"] = "HEAVILY_PLAYED",
        ["DMG"] = "DAMAGED",
        ["DAMAGED"] = "DAMAGED",
        ["UNKNOWN"] = "UNKNOWN",
        ["UNSPECIFIED"] = "UNKNOWN",
    };

    public static string Condition(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("Condition is required.");
        var normalized = CodeRegex().Replace(value.Trim().ToUpperInvariant(), "_").Trim('_');
        return ConditionAliases.TryGetValue(normalized, out var canonical)
            ? canonical
            : throw new DomainException($"Unsupported condition code: '{value}'. Supported values: {string.Join(", ", CanonicalConditions)}.");
    }

    public static void ValidateAcquisitionPrice(Money? price)
    {
        if (price is { Amount: < 0 }) throw new DomainException("Acquisition price cannot be negative.");
    }

    public static string AssetType(string value)
    {
        var type = value.Trim().ToUpperInvariant();
        return type is "FRONT" or "BACK" or "DETAIL" or "OTHER" ? type : throw new DomainException("Unsupported collectible item asset type.");
    }

    public static string? Notes(string? value)
    {
        if (value is null) return null;
        var notes = value.Trim();
        if (notes.Length > 500) throw new DomainException("Notes cannot exceed 500 characters.");
        return notes.Length == 0 ? null : notes;
    }

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex CodeRegex();
}
