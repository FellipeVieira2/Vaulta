namespace Vaulta.App.Core.Catalog;

public readonly record struct ScannerCaptureGuide(double Left, double Top, double Width, double Height)
{
    // Full-preview coordinates shared with the presence detector. Leave room
    // around all four edges; the detector cannot read a card clipped by the view.
    public static ScannerCaptureGuide ForPreview(double width, double height)
    {
        var cardHeight = Math.Min(height * (width > height ? .84 : .72), width * .84 / .715);
        var cardWidth = cardHeight * .715;
        return new((width - cardWidth) / 2, (height - cardHeight) / 2, cardWidth, cardHeight);
    }
}
