using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Catalog.Infrastructure.Recognition;
using Vaulta.Identity.Contracts;
using Vaulta.SharedKernel;
using Xunit;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class OpenAiScannerFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task ImageToCanonicalPrintingAndCachedDetailsUsesOnlyFakeOpenAiBoundary()
    {
        using var boundary = new Boundary(); var ocr = new Ocr(); var visual=new ArtificialVisualBoundary();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IImageEncoder>(); services.AddSingleton<IImageEncoder>(visual);
            services.RemoveAll<IVisualReferenceIndex>(); services.AddSingleton<IVisualReferenceIndex>(visual);
            services.RemoveAll<IOcrService>(); services.AddSingleton<IOcrService>(ocr);
            services.RemoveAll<OpenAiCardEvidenceExtractor>();
            services.AddSingleton(_=>new OpenAiCardEvidenceExtractor(new HttpClient(boundary,false) { BaseAddress=new("https://api.openai.com/v1/") },
                Options.Create(new OpenAiScannerOptions { Enabled=true,ApiKey="fake-test-key",RetryCount=0 }),NullLogger<OpenAiCardEvidenceExtractor>.Instance));
            services.RemoveAll<IScannerCardDetailsReader>(); services.AddScoped<IScannerCardDetailsReader,LocalScannerDetailsReader>();
            services.AddHttpClient<ICatalogProvider,TcgDexProvider>().ConfigurePrimaryHttpMessageHandler(()=>new NoNetwork());
        }));
        using var client = factory.CreateClient();
        var provider = new Provider(); boundary.CardName=provider.CardName; boundary.SetName=provider.SetName; ocr.Name=provider.CardName; Guid printingId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await new CatalogSyncService(db, [provider], scope.ServiceProvider.GetRequiredService<IClock>(), NullLogger<CatalogSyncService>.Instance).Synchronize(provider.Code, provider.SetId, default);
            printingId = await db.ExternalIds.Where(x => x.EntityType == "printing" && x.ExternalId == provider.CardId).Select(x => x.EntityId).SingleAsync();
            var printing = (await scope.ServiceProvider.GetRequiredService<ICatalogSearch>().GetPrinting(printingId, default))!;
            var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow; var day = MarketPriceDay.At(now);
            var details = new ScannerCardDetailsDto(printing, new Dictionary<string, string>(), [], "Cotação indisponível", now, day.RefreshAfter);
            db.DailyMarketSnapshots.Add(new() { PrintingId = printingId, MarketDay = day.Date, FetchedAt = now, RefreshAfter = day.RefreshAfter, Payload = JsonSerializer.Serialize(details) });
            await db.SaveChangesAsync();
        }
        visual.PrintingId=printingId;
        var register = new RegisterRequest($"{Guid.NewGuid():N}@example.test", "Secure-Test-Password1!", "s" + Guid.NewGuid().ToString("N")[..20], "Collector");
        (await client.PostAsJsonAsync("/api/v1/auth/register", register)).EnsureSuccessStatusCode();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(register.Email, register.Password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken);
        for (var i = 0; i < 2; i++)
        {
            boundary.Certified = i == 1;
            using var multipart = new MultipartFormDataContent();
            using var image = new Image<Rgba32>(200, 300); using var bytes = new MemoryStream(); image.SaveAsPng(bytes);
            var content = new ByteArrayContent(bytes.ToArray()); content.Headers.ContentType = new("image/png"); multipart.Add(content, "image", "card.png");
            using var response = await client.PostAsync("/api/v1/scanner/identify?gameCode=pokemon", multipart);
            response.EnsureSuccessStatusCode();
            var result=(await response.Content.ReadFromJsonAsync<VisionScanResultDto>())!;
            Assert.Equal("identified",result.Status); Assert.Equal(printingId,result.PrintingId); Assert.Null(result.Price);
            Assert.Equal(.8,result.PrintingConfidence); Assert.Equal("normal",result.Evidence["variant"].Value);
            if(i==1) { Assert.Equal("PSA",result.Evidence["gradingCompany"].Value); Assert.Equal("01234567",result.Evidence["certificationNumber"].Value); Assert.Equal("graded_unavailable",result.PriceStatus); }
        }
        Assert.Equal(2, boundary.Calls); Assert.Equal(2, ocr.Calls);
        var detailsResponses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.GetAsync($"/api/v1/scanner/printings/{printingId}")));
        foreach (var response in detailsResponses)
        {
            using (response) { response.EnsureSuccessStatusCode(); Assert.NotNull((await response.Content.ReadFromJsonAsync<ScannerCardDetailsDto>())!.FetchedAt); }
        }
        // This new capture fails at the external boundary and uses real OCR matching.
        boundary.Invalid = true;
        using var fallbackImage = new Image<Rgba32>(200, 300); using var fallbackBytes = new MemoryStream(); fallbackImage.SaveAsPng(fallbackBytes);
        using var fallbackForm = new MultipartFormDataContent(); var fallbackContent = new ByteArrayContent(fallbackBytes.ToArray());
        fallbackContent.Headers.ContentType = new("image/png"); fallbackForm.Add(fallbackContent, "image", "card.png");
        using var fallbackResponse = await client.PostAsync("/api/v1/scanner/identify?gameCode=pokemon", fallbackForm);
        fallbackResponse.EnsureSuccessStatusCode();
        var fallback=(await fallbackResponse.Content.ReadFromJsonAsync<VisionScanResultDto>())!;
        Assert.Contains(fallback.Candidates,x=>x.Printing.PrintingId==printingId); Assert.Equal("partial",fallback.Status); Assert.Null(fallback.Price);
        Assert.Equal(3,ocr.Calls);
    }

    // Artificial image boundary isolates HTTP orchestration; the separate real encoder harness proves inference.
    private sealed class ArtificialVisualBoundary : IImageEncoder,IVisualReferenceIndex
    {
        public Guid PrintingId; public EncoderIdentity Identity { get; }=new("fixture","fixture","fixture","fixture",3,"test","input","output");
        public VisualIndexStatus Status=>new("fixture",1,Identity);
        public Task<ImageEmbedding> EncodeAsync(Stream image,CancellationToken ct)=>Task.FromResult(new ImageEmbedding(Identity,[1f,0f,0f]));
        public Task<IReadOnlyList<VisualMatch>> SearchAsync(ImageEmbedding embedding,int topK,CancellationToken ct)=>Task.FromResult<IReadOnlyList<VisualMatch>>([new(Guid.NewGuid(),PrintingId,.99,"fixture")]);
    }
    private sealed class Boundary : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool Invalid { get; set; }
        public bool Certified { get; set; }
        public string CardName { get; set; }=null!; public string SetName { get; set; }=null!;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            using var requestBody=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.False(requestBody.RootElement.TryGetProperty("tools",out _));
            var context=requestBody.RootElement.GetProperty("input")[0].GetProperty("content")[1].GetProperty("text").GetString()!;
            Assert.Contains("Untrusted local retrieval candidates",context); Assert.DoesNotContain("PrintingId",context); Assert.DoesNotContain("MarketValue",context);
            Calls++;
            const string evidence = """
                {"schemaVersion":1,"gameCode":{"value":"pokemon","confidence":0.8},"name":{"value":"OpenAi Scanner Test Card","confidence":0.8},"collectorNumber":{"value":"058/102","confidence":0.8},"setCode":{"value":null,"confidence":0},"setName":{"value":null,"confidence":0},"language":{"value":"en","confidence":0.8},"variant":{"value":"normal","confidence":0.8}}
                """;
            var reading = JsonNode.Parse(evidence)!; reading["name"]!["value"]=CardName; reading["setName"]=new JsonObject { ["value"]=SetName,["confidence"]=.8 };
            if (Certified)
            {
                reading["schemaVersion"] = 4;
                foreach (var field in new[] { "hp", "finish", "condition", "isGraded", "gradingCompany", "grade", "certificationNumber", "rarity", "year", "cardType", "stage" })
                    reading[field] = new JsonObject { ["value"] = null, ["confidence"] = 0 };
                reading["isGraded"] = new JsonObject { ["value"] = "true", ["confidence"] = .99 };
                reading["gradingCompany"] = new JsonObject { ["value"] = "PSA", ["confidence"] = .99 };
                reading["grade"] = new JsonObject { ["value"] = "10", ["confidence"] = .99 };
                reading["certificationNumber"] = new JsonObject { ["value"] = "01234567", ["confidence"] = .99 };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            { status = "completed", output = new[] { new { type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text = Invalid ? "{}" : reading.ToJsonString() } } } } })) };
        }
    }
    private sealed class Ocr : IOcrService
    {
        public int Calls { get; private set; } public string Name { get; set; }=null!;
        public Task<OcrResult> ExtractTextAsync(byte[] bytes, CancellationToken ct) { Calls++; return Task.FromResult(new OcrResult(Name+"\n058/102", [])); }
    }
    private sealed class NoNetwork : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("Snapshot must prevent external pricing/details HTTP"); }
    private sealed class NoRates : IBrlExchangeRateProvider
    { public Task<BrlExchangeRate?> GetAsync(string currency, CancellationToken ct) => throw new InvalidOperationException("Snapshot must prevent external FX"); }
    private sealed class Provider : ICatalogProvider
    {
        public string Code => "tcgdex"; public string SetId { get; } = "openai-" + Guid.NewGuid().ToString("N"); public string CardId => SetId + "-58";
        public string CardName=>"OpenAi Scanner Test Card "+SetId; public string SetName=>"OpenAI test set "+SetId;
        private ProviderSet Set => new(SetId,SetName,null,null);
        public Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken ct) => Task.FromResult<IReadOnlyList<ProviderSet>>([Set]);
        public Task<ProviderSetDetails> GetSetDetails(string id, CancellationToken ct) => Task.FromResult(new ProviderSetDetails(Set,
            [new(CardId,CardName, "58/102", "en", "Common", null, [new("normal", "Normal", "normal"), new("holo", "Holo", "holo")])]));
    }
}
