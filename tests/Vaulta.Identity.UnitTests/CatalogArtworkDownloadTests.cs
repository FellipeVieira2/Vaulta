using System.Net;
using System.Net.Http.Headers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vaulta.Catalog.Infrastructure.Artwork;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public sealed class CatalogArtworkDownloadTests
{
    private const string Url="https://assets.tcgdex.net/en/base/base1/1/high.png";
    [Fact]
    public async Task DownloadsValidArtworkAndDoesNotUpscaleRenditions()
    {
        using var image=new Image<Rgb24>(30,40); using var data=new MemoryStream(); image.SaveAsPng(data);
        var bytes=data.ToArray();
        var response=new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(bytes) };
        response.Headers.ETag=new EntityTagHeaderValue("\"revision-1\"");
        using var http=new HttpClient(new Handler(_=>response));
        var result=await new CatalogArtworkDownloader(http).DownloadAsync(Url,null,null,default);
        Assert.Equal("ready",result.Status); Assert.Equal(30,result.Width); Assert.Equal(40,result.Height);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),result.SourceSha256);
        using var thumb=Image.Load(result.Thumbnail!); Assert.Equal(30,thumb.Width); Assert.Equal(40,thumb.Height);
    }
    [Fact]
    public async Task ConditionalDownloadUsesEtagAndPreservesNotModified()
    {
        using var http=new HttpClient(new Handler(request=> {
            Assert.Equal("\"revision-1\"",request.Headers.IfNoneMatch.Single().ToString());
            return new(HttpStatusCode.NotModified);
        }));
        var result=await new CatalogArtworkDownloader(http).DownloadAsync(Url,"\"revision-1\"",null,default);
        Assert.Equal("not_modified",result.Status); Assert.Null(result.Image);
    }
    [Fact]
    public async Task MissingArtworkIsExplicit()
    {
        using var http=new HttpClient(new Handler(_=>new(HttpStatusCode.NotFound)));
        Assert.Equal("missing",(await new CatalogArtworkDownloader(http).DownloadAsync(Url,null,null,default)).Status);
    }
    [Fact]
    public async Task InvalidImageAndUntrustedUrlAreRejected()
    {
        var requests=0;
        using var http=new HttpClient(new Handler(_=> { requests++; return new(HttpStatusCode.OK) { Content=new StringContent("not an image") }; }));
        var download=new CatalogArtworkDownloader(http);
        await Assert.ThrowsAsync<InvalidDataException>(()=>download.DownloadAsync(Url,null,null,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>download.DownloadAsync("https://127.0.0.1/private",null,null,default));
        Assert.Equal(1,requests);
    }
    [Fact]
    public async Task TemporaryProviderFailureRetriesBeforeMarkingArtworkFailed()
    {
        var calls=0; using var image=new Image<Rgb24>(30,40); using var data=new MemoryStream(); image.SaveAsPng(data);
        using var http=new HttpClient(new Handler(_=>++calls==1 ? new(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.OK) { Content=new ByteArrayContent(data.ToArray()) }));
        Assert.Equal("ready",(await new CatalogArtworkDownloader(http).DownloadAsync(Url,null,null,default)).Status);
        Assert.Equal(2,calls);
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(handle(request)); }
}
