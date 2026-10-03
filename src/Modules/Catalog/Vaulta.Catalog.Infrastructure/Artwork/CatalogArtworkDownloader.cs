using System.Net;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
namespace Vaulta.Catalog.Infrastructure.Artwork;
public sealed record DownloadedArtwork(string Status, byte[]? Image, byte[]? Thumbnail, string? SourceSha256, string? ETag, DateTimeOffset? LastModified, int Width, int Height);
public sealed class CatalogArtworkDownloader(HttpClient http)
{
    public async Task<DownloadedArtwork> DownloadAsync(string sourceUrl, string? etag, DateTimeOffset? modified, CancellationToken ct)
    {
        for(var attempt=0;;attempt++)
        {
            try { return await DownloadOnce(sourceUrl,etag,modified,ct); }
            catch(HttpRequestException error) when(attempt<3 && (error.StatusCode is null || error.StatusCode==(HttpStatusCode)429 || (int)error.StatusCode>=500))
            {
                var delay=error is RetryableArtworkException retry ? retry.Delay : TimeSpan.FromMilliseconds(500*Math.Pow(2,attempt));
                if(delay>TimeSpan.FromSeconds(30)) throw;
                await Task.Delay(delay<TimeSpan.Zero ? TimeSpan.Zero : delay,ct);
            }
        }
    }
    private async Task<DownloadedArtwork> DownloadOnce(string sourceUrl, string? etag, DateTimeOffset? modified, CancellationToken ct)
    {
        if(!Uri.TryCreate(sourceUrl,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!="assets.tcgdex.net" || !uri.IsDefaultPort || uri.UserInfo.Length>0)
            throw new ArgumentException("Artwork URL is not an approved provider endpoint.");
        using var request=new HttpRequestMessage(HttpMethod.Get,uri);
        if(etag is not null) request.Headers.IfNoneMatch.ParseAdd(etag);
        else if(modified is not null) request.Headers.IfModifiedSince=modified;
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode==HttpStatusCode.NotModified) return new("not_modified",null,null,null,etag,modified,0,0);
        if(response.StatusCode==HttpStatusCode.NotFound) return new("missing",null,null,null,null,null,0,0);
        if(response.StatusCode==(HttpStatusCode)429 || (int)response.StatusCode>=500)
        {
            var delay=response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date-DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1));
            throw new RetryableArtworkException(response.StatusCode,delay);
        }
        response.EnsureSuccessStatusCode();
        const int maxBytes=15*1024*1024;
        if(response.Content.Headers.ContentLength>maxBytes) throw new InvalidDataException("Artwork exceeds the byte limit.");
        await using var stream=await response.Content.ReadAsStreamAsync(ct);
        using var bytes=new MemoryStream(); var buffer=new byte[16384]; int read;
        while((read=await stream.ReadAsync(buffer,ct))>0)
        { if(bytes.Length+read>maxBytes) throw new InvalidDataException("Artwork exceeds the byte limit."); await bytes.WriteAsync(buffer.AsMemory(0,read),ct); }
        var original=bytes.ToArray();
        try
        {
            var info=Image.Identify(original);
            if(info.Width<1 || info.Height<1 || (long)info.Width*info.Height>20_000_000) throw new InvalidDataException("Artwork dimensions exceed the pixel limit.");
            using var image=Image.Load(new DecoderOptions { MaxFrames=1 },original);
            if(image.Metadata.DecodedImageFormat?.DefaultMimeType is not ("image/png" or "image/jpeg" or "image/webp")) throw new InvalidDataException("Unsupported artwork format.");
            var rendition=Render(image,600,825); var thumbnail=Render(image,200,275);
            return new("ready",rendition,thumbnail,Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),response.Headers.ETag?.ToString(),response.Content.Headers.LastModified,info.Width,info.Height);
        }
        catch(UnknownImageFormatException error) { throw new InvalidDataException("Provider returned invalid artwork.",error); }
        catch(InvalidImageContentException error) { throw new InvalidDataException("Provider returned corrupt artwork.",error); }
    }
    private sealed class RetryableArtworkException(HttpStatusCode status,TimeSpan delay) : HttpRequestException("Temporary artwork provider failure.",null,status)
    { public TimeSpan Delay { get; }=delay; }
    private static byte[] Render(Image image,int maxWidth,int maxHeight)
    {
        using var rendition=image.Clone(context=>context.Resize(new ResizeOptions { Size=new(Math.Min(image.Width,maxWidth),Math.Min(image.Height,maxHeight)),Mode=ResizeMode.Max,Sampler=KnownResamplers.Bicubic }));
        using var output=new MemoryStream(); rendition.Save(output,new WebpEncoder { Quality=90 }); return output.ToArray();
    }
}
