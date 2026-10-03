using CommunityToolkit.Maui.Views;
using Vaulta.App.Core.Catalog;

namespace Vaulta.App.Services.Camera;

public static class CameraSceneSampler
{
    public sealed record Observation(byte[] Signature, bool CardPresent, byte[]? Image = null, double Sharpness = 0,byte[]? FullImage=null);

    public static byte[]? Read(CameraView camera) => ReadObservation(camera)?.Signature;

    public static Observation? ReadObservation(CameraView camera, bool includeFrame = false)
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
        var region=ScannerCardPresence.Locate(gray,width,height);
        if(region is null) return new(signature,false);
        double sum=0,squared=0; var count=0;
        for(var y=region.Top+3;y<region.Top+region.Height-3;y++)
            for(var x=region.Left+3;x<region.Left+region.Width-3;x++)
            { var lap=4*gray[y*width+x]-gray[y*width+x-1]-gray[y*width+x+1]-gray[(y-1)*width+x]-gray[(y+1)*width+x]; sum+=lap; squared+=(double)lap*lap; count++; }
        var sharpness=count==0 ? 0 : squared/count-Math.Pow(sum/count,2);
        if(!includeFrame) return new(signature,true,null,sharpness);
        // Preserve the exact useful preview frame, including a small margin around its localized edges.
        var left=Math.Clamp((int)((region.Left-2)/scale),0,image.Width-1);
        var top=Math.Clamp((int)((region.Top-2)/scale),0,image.Height-1);
        var right=Math.Clamp((int)((region.Left+region.Width+2)/scale),left+1,image.Width);
        var bottom=Math.Clamp((int)((region.Top+region.Height+2)/scale),top+1,image.Height);
        using var crop=Android.Graphics.Bitmap.CreateBitmap(image,left,top,right-left,bottom-top);
        using var saved=new MemoryStream();
        if(crop is null || !crop.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!,92,saved)) return null;
        // Keep the full selected frame for GPT/OCR: a slab label can sit outside the card artwork.
        using var full=new MemoryStream();
        if(!image.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!,92,full)) return null;
        return new(signature,true,saved.ToArray(),sharpness,full.ToArray());
#else
        return null;
#endif
    }
}
