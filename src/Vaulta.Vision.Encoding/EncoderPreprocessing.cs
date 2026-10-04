using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
namespace Vaulta.Vision.Encoding;

public static class EncoderPreprocessing
{
    public static void Apply(Image<Rgb24> image,EncoderManifest manifest)
    {
        image.Mutate(context=>context.AutoOrient());
        if(manifest.PreprocessingVersion==EncoderManifest.LetterboxPreprocessing)
            image.Mutate(context=>context.Resize(new ResizeOptions{Size=new(224,224),Mode=ResizeMode.Pad,Sampler=KnownResamplers.Bicubic,PadColor=Color.FromRgb(127,127,127)}));
        else if(manifest.PreprocessingVersion==EncoderManifest.DirectResizePreprocessing)
            image.Mutate(context=>context.Resize(224,224,KnownResamplers.Bicubic));
        else
        {
            var edge=manifest.Preprocessing.GetProperty("size").GetProperty("shortest_edge").GetInt32();
            var scale=edge/(double)Math.Min(image.Width,image.Height);
            var width=Math.Max(224,(int)(image.Width*scale));var height=Math.Max(224,(int)(image.Height*scale));
            image.Mutate(context=>context.Resize(width,height,KnownResamplers.Bicubic).Crop(new Rectangle((width-224)/2,(height-224)/2,224,224)));
        }
    }
}
