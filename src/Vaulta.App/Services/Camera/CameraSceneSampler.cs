using CommunityToolkit.Maui.Views;
using Vaulta.App.Core.Catalog;

namespace Vaulta.App.Services.Camera;

public static class CameraSceneSampler
{
    public sealed record Observation(byte[] Signature, bool CardPresent);

    public static byte[]? Read(CameraView camera) => ReadObservation(camera)?.Signature;

    public static Observation? ReadObservation(CameraView camera)
    {
#if ANDROID
        var preview = camera.Handler?.PlatformView as AndroidX.Camera.View.PreviewView;
        if (preview?.PreviewStreamState.Value?.ToString() != "STREAMING") return null;
        using var image = preview.Bitmap;
        if (image is null || image.Width < 24 || image.Height < 24) return null;
        // Sample the central card area. No camera frame is stored or uploaded here.
        var signature = new byte[8 * 12 * 3]; var index = 0;
        for (var y = 0; y < 12; y++)
            for (var x = 0; x < 8; x++)
            {
                var pixel = image.GetPixel((int)((.25 + (x + .5) / 8 * .5) * image.Width),
                    (int)((.25 + (y + .5) / 12 * .5) * image.Height));
                signature[index++] = (byte)((pixel >> 16) & 255);
                signature[index++] = (byte)((pixel >> 8) & 255);
                signature[index++] = (byte)(pixel & 255);
            }
        // Preserve aspect ratio in a tiny grayscale preview. No ML download,
        // encoded photo, persistence or network request is needed for presence.
        var scale = 160d / Math.Max(image.Width, image.Height);
        var width = Math.Max(24, (int)Math.Round(image.Width * scale));
        var height = Math.Max(24, (int)Math.Round(image.Height * scale));
        using var thumbnail = Android.Graphics.Bitmap.CreateScaledBitmap(image, width, height, true);
        if (thumbnail is null) return null;
        var pixels = new int[width * height];
        thumbnail.GetPixels(pixels, 0, width, 0, 0, width, height);
        var gray = new byte[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
            gray[i] = (byte)((77 * ((pixels[i] >> 16) & 255) + 150 * ((pixels[i] >> 8) & 255) + 29 * (pixels[i] & 255)) >> 8);
        return new(signature, ScannerCardPresence.IsPresent(gray, width, height));
#else
        return null;
#endif
    }
}
