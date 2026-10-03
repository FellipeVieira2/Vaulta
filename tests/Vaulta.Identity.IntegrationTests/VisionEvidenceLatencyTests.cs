using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Infrastructure.Recognition;
using Vaulta.Vision.Infrastructure;
using Xunit;
namespace Vaulta.Identity.IntegrationTests;
public sealed class VisionEvidenceLatencyTests
{
    [Fact] public async Task SlowOcrIsCancelledAndCannotHoldTheGptFallbackIndefinitely()
    {
        var ocr=new NeverCompletes(); using var http=new HttpClient(new NoCalls()) { BaseAddress=new("https://api.openai.com/v1/") };
        using var gpt=new OpenAiCardEvidenceExtractor(http,Options.Create(new OpenAiScannerOptions { Enabled=false }),NullLogger<OpenAiCardEvidenceExtractor>.Instance);
        var reading=await new VisionEvidenceReader(ocr,gpt).ReadAsync([1],[],default).WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(ocr.Cancelled); Assert.Equal("not_configured",reading.Issue); Assert.Null(reading.Evidence);
    }
    private sealed class NeverCompletes : IOcrService
    {
        public bool Cancelled;
        public async Task<OcrResult> ExtractTextAsync(byte[] image,CancellationToken ct)
        { try { await Task.Delay(Timeout.Infinite,ct); return new("",[]); } finally { Cancelled=ct.IsCancellationRequested; } }
    }
    private sealed class NoCalls : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>throw new InvalidOperationException("Disabled GPT must not make HTTP calls."); }
}
