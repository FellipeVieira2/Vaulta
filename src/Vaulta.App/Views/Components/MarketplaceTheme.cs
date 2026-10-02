namespace Vaulta.App.Views.Components;

/// <summary>Compact Figma marketplace tokens; the existing catalog retains its theme.</summary>
internal static class MarketplaceTheme
{
    public const string RegularFont = "InterRegular";
    public const string MediumFont = "InterMedium";
    public const string SemiBoldFont = "InterSemiBold";
    public const string BoldFont = "InterBold";
    public static Color Background => Color("PrimitiveNeutral950");
    public static Color Surface => Color("PrimitiveNeutral900");
    public static Color Border => Color("PrimitiveNeutral700");
    public static Color Primary => Color("PrimitiveWhite");
    public static Color Secondary => Color("PrimitiveNeutral400");
    public static Color Brand => Color("PrimitiveBrand400");
    public static Color Error => Color("PrimitiveError400");
    private static Color Color(string key) => (Color)Application.Current!.Resources[key];

    public static Label Text(string? text, double size = 13, bool bold = false, bool secondary = false) => new()
    {
        Text = text, FontSize = size, FontFamily = bold ? SemiBoldFont : RegularFont,
        TextColor = secondary ? Secondary : Primary, LineHeight = 1.3,
        FontAutoScalingEnabled = true
    };

    public static Button Action(string text) => new()
    {
        Text = text, BackgroundColor = Colors.Transparent, TextColor = Brand,
        FontSize = 13, FontFamily = SemiBoldFont, Padding = new Thickness(8, 0),
        MinimumHeightRequest = 48, CornerRadius = 8, BorderWidth = 0,
        FontAutoScalingEnabled = true
    };
}
