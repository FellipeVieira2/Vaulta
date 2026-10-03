using Vaulta.App.Core.Catalog;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class ScannerCardPresenceTests
{
    [Theory]
    [InlineData(90, 160)]
    [InlineData(96, 128)]
    [InlineData(72, 160)]
    [InlineData(160, 72)]
    public void CardAlignedWithTheActualCameraGuideIsDetected(int width, int height)
    {
        var guide = ScannerCaptureGuide.ForPreview(width, height);
        var image = Enumerable.Repeat((byte)35, width * height).ToArray();
        var left = (int)Math.Round(guide.Left); var top = (int)Math.Round(guide.Top);
        var right = (int)Math.Round(guide.Left + guide.Width); var bottom = (int)Math.Round(guide.Top + guide.Height);
        for (var y = top; y < bottom; y++) for (var x = left; x < right; x++)
            image[y * width + x] = x > left + 5 && x < right - 5 && y > top + 5 && y < bottom - 5 && (x / 4 + y / 5) % 2 == 0 ? (byte)135 : (byte)180;
        Assert.True(ScannerCardPresence.IsPresent(image, width, height));
    }

    private const int Width = 96, Height = 128;

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(180)]
    [InlineData(255)]
    public void EmptyBackgroundNeverLooksLikeCard(byte brightness) =>
        Assert.False(ScannerCardPresence.IsPresent(Enumerable.Repeat(brightness, Width * Height).ToArray(), Width, Height));

    [Fact]
    public void GradientAndTexturedBackgroundsAreRejected()
    {
        var gradient = new byte[Width * Height]; var noise = new byte[Width * Height];
        var random = new Random(42);
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
        { gradient[y * Width + x] = (byte)(40 + x + y / 2); noise[y * Width + x] = (byte)random.Next(256); }
        Assert.False(ScannerCardPresence.IsPresent(gradient, Width, Height));
        Assert.False(ScannerCardPresence.IsPresent(noise, Width, Height));
    }

    [Theory]
    [InlineData(35, 180)]
    [InlineData(210, 60)]
    public void CenteredCardWithFourEdgesAndPrintedDetailIsAccepted(byte background, byte face) =>
        Assert.True(ScannerCardPresence.IsPresent(Rectangle(20, 25, 76, 103, background, face), Width, Height));

    [Fact]
    public void BlankPaperSquareTooSmallAndClippedObjectsAreRejected()
    {
        Assert.False(ScannerCardPresence.IsPresent(Rectangle(20, 25, 76, 103, 35, 180, false), Width, Height));
        Assert.False(ScannerCardPresence.IsPresent(Rectangle(16, 32, 80, 96, 35, 180), Width, Height));
        Assert.False(ScannerCardPresence.IsPresent(Rectangle(40, 50, 56, 74, 35, 180), Width, Height));
        Assert.False(ScannerCardPresence.IsPresent(Rectangle(0, 10, 56, 88, 35, 180), Width, Height));
    }

    [Fact]
    public void OvalHandLikeObjectAndInsufficientContrastAreRejected()
    {
        var image = Rectangle(20, 25, 76, 103, 35, 180);
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
            if (Math.Pow((x - 48) / 28d, 2) + Math.Pow((y - 64) / 39d, 2) > 1) image[y * Width + x] = 35;
        Assert.False(ScannerCardPresence.IsPresent(image, Width, Height));
        Assert.False(ScannerCardPresence.IsPresent(Rectangle(20, 25, 76, 103, 175, 180), Width, Height));
    }

    [Fact]
    public void InvalidPreviewIsRejectedWithoutException()
    {
        Assert.False(ScannerCardPresence.IsPresent([], Width, Height));
        Assert.False(ScannerCardPresence.IsPresent(new byte[8], int.MaxValue, int.MaxValue));
    }

    private static byte[] Rectangle(int left, int top, int right, int bottom, byte background, byte face, bool printed = true)
    {
        var image = Enumerable.Repeat(background, Width * Height).ToArray();
        for (var y = top; y < bottom; y++) for (var x = left; x < right; x++)
        {
            var ink = printed && x > left + 5 && x < right - 5 && y > top + 5 && y < bottom - 5 && (x / 4 + y / 5) % 2 == 0;
            image[y * Width + x] = ink ? (byte)Math.Clamp(face - 45, 0, 255) : face;
        }
        return image;
    }
}
