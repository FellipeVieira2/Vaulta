using System.Text.RegularExpressions;
using Vaulta.SharedKernel;

namespace Vaulta.Collection.Domain;

public static partial class CollectionRules
{
    public const string ActiveStatus = "ACTIVE";
    public const string RemovedStatus = "REMOVED";

    public static string Condition(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("Condition is required.");
        var code = CodeRegex().Replace(value.Trim().ToUpperInvariant(), "_");
        if (code.Length > 32 || code.Length == 0) throw new DomainException("Condition code is invalid.");
        return code;
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
