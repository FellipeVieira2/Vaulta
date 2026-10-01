using CommunityToolkit.Maui.Views;

namespace Vaulta.App.Services.Camera;

public static class CameraSceneSampler
{
    public static byte[]? Read(CameraView camera)
    {
#if ANDROID
        var preview = camera.Handler?.PlatformView as AndroidX.Camera.View.PreviewView;
        if (preview?.PreviewStreamState.Value?.ToString() != "STREAMING") return null;
        using var image = preview.Bitmap;
        if (image is null || image.Width < 8 || image.Height < 8) return null;
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
        return signature;
#else
        return null;
#endif
    }
}
