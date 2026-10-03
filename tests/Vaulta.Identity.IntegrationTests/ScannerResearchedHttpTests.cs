using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Identity.Contracts;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Xunit;
namespace Vaulta.Identity.IntegrationTests;
[Collection("api")]
public sealed class ScannerResearchedHttpTests(ApiFixture fixture)
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task UnknownLocalPrintingKeepsVisualEvidenceWithoutWebPriceOrInventedIdentity(bool readableNumber)
    {
        var visual=new Visual(); var evidence=new Evidence(readableNumber);
        await using var factory=fixture.Factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
        {
            services.RemoveAll<IImageEncoder>(); services.AddSingleton<IImageEncoder>(visual);
            services.RemoveAll<IVisualReferenceIndex>(); services.AddSingleton<IVisualReferenceIndex>(visual);
            services.RemoveAll<IVisionEvidenceReader>(); services.AddSingleton<IVisionEvidenceReader>(evidence);
            services.RemoveAll<IScannerMarketResearch>(); services.AddSingleton<IScannerMarketResearch,NoResearch>();
            services.RemoveAll<IScannerWebMarketResearchProvider>(); services.AddSingleton<IScannerWebMarketResearchProvider,NoWebPrice>();
            services.RemoveAll<ICardEvidenceCatalogEnricher>(); services.AddSingleton<ICardEvidenceCatalogEnricher,NoCatalogHttp>();
        }));
        using var client=factory.CreateClient();
        var register=new RegisterRequest($"{Guid.NewGuid():N}@example.test","Secure-Test-Password1!","s"+Guid.NewGuid().ToString("N")[..20],"Collector");
        (await client.PostAsJsonAsync("/api/v1/auth/register",register)).EnsureSuccessStatusCode();
        using var login=await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest(register.Email,register.Password)); login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken);
        using var form=new MultipartFormDataContent(); var content=new ByteArrayContent([1,2,3]); content.Headers.ContentType=new("image/png"); form.Add(content,"image","fixture.png");
        using var response=await client.PostAsync("/api/v1/scanner/identify",form); response.EnsureSuccessStatusCode();
        var result=(await response.Content.ReadFromJsonAsync<VisionScanResultDto>())!;
        Assert.Equal("not_in_catalog",result.Status); Assert.Null(result.PrintingId); Assert.Null(result.VariantId); Assert.Null(result.Price);
        Assert.Equal(evidence.Name,result.Evidence["name"].Value); Assert.Equal(readableNumber?"067/086":null,result.Evidence["collectorNumber"].Value);
        Assert.Equal("unresolved",result.PriceStatus);
    }
    // Artificial encoder isolates HTTP policy; real weights have a separate functional harness.
    private sealed class Visual : IImageEncoder,IVisualReferenceIndex
    {
        public EncoderIdentity Identity { get; }=new("fixture","fixture","fixture","fixture",3,"test","input","output");
        public VisualIndexStatus Status=>new("empty-fixture",0,Identity);
        public Task<ImageEmbedding> EncodeAsync(Stream image,CancellationToken ct)=>Task.FromResult(new ImageEmbedding(Identity,[1f,0f,0f]));
        public Task<IReadOnlyList<VisualMatch>> SearchAsync(ImageEmbedding embedding,int topK,CancellationToken ct)=>Task.FromResult<IReadOnlyList<VisualMatch>>([]);
    }
    private sealed class Evidence(bool number) : IVisionEvidenceReader
    {
        public string Name { get; }="Unknown card "+Guid.NewGuid().ToString("N");
        public Task<VisionEvidenceReading> ReadAsync(byte[] image,IReadOnlyList<VisionCatalogPrinting> candidates,CancellationToken ct)
        { CardEvidenceField F(string? value)=>new(value,value is null?0:.96); return Task.FromResult(new VisionEvidenceReading(new(F("pokemon"),F(Name),F(number?"067/086":null),F(null),F(null),F("en"),F("normal"),"fixture","fixture",F("140")),null,null)); }
    }
    private sealed class NoResearch : IScannerMarketResearch
    { public Task<ScannerMarketResearchResultDto?> ResearchAsync(CardVisualIdentificationDto card,byte[]? image,CancellationToken ct)=>throw new InvalidOperationException("Scan must not research external prices."); }
    private sealed class NoWebPrice : IScannerWebMarketResearchProvider
    { public Task<ScannerMarketResearchResultDto?> ResearchAsync(CardVisualIdentificationDto card,byte[]? image,CancellationToken ct)=>throw new InvalidOperationException("Scan must not call JustTCG/GPT web pricing."); }
    private sealed class NoCatalogHttp : ICardEvidenceCatalogEnricher
    { public Task<bool> EnrichAsync(CardEvidence evidence,string game,CancellationToken ct)=>throw new InvalidOperationException("Scan must not import a provider catalog."); }
}
