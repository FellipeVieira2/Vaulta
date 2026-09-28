namespace Vaulta.App.Core.Collection;

/// <summary>
/// Single source of truth mapping the UI's condition labels to the canonical backend codes
/// (mirrors Vaulta.Collection.Domain.CollectionRules.CanonicalConditions). The client never
/// sends arbitrary UI text as a condition; unmapped input resolves to UNKNOWN rather than
/// being invented.
/// </summary>
public static class ConditionMapper
{
    public const string Mint = "MINT";
    public const string NearMint = "NEAR_MINT";
    public const string LightlyPlayed = "LIGHTLY_PLAYED";
    public const string ModeratelyPlayed = "MODERATELY_PLAYED";
    public const string HeavilyPlayed = "HEAVILY_PLAYED";
    public const string Damaged = "DAMAGED";
    public const string Unknown = "UNKNOWN";

    private static readonly IReadOnlyDictionary<string, string> LabelToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Mint"] = Mint,
        ["Near Mint"] = NearMint,
        ["Lightly Played"] = LightlyPlayed,
        ["Moderately Played"] = ModeratelyPlayed,
        ["Heavily Played"] = HeavilyPlayed,
        ["Damaged"] = Damaged,
    };

    /// <summary>The condition chips shown to the user, in display order.</summary>
    public static IReadOnlyList<string> UiLabels { get; } =
        ["Mint", "Near Mint", "Lightly Played", "Moderately Played", "Heavily Played", "Damaged"];

    public static string ToCanonicalCode(string uiLabel) =>
        LabelToCode.TryGetValue(uiLabel.Trim(), out var code) ? code : Unknown;

    public static string ToUiLabel(string canonicalCode) => canonicalCode.Trim().ToUpperInvariant() switch
    {
        Mint => "Mint",
        NearMint => "Near Mint",
        LightlyPlayed => "Lightly Played",
        ModeratelyPlayed => "Moderately Played",
        HeavilyPlayed => "Heavily Played",
        Damaged => "Damaged",
        _ => "Unknown"
    };
}
