using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Catalog.Infrastructure.MarketResearch;
using Vaulta.Catalog.Infrastructure.Recognition;
using Vaulta.Identity.Contracts;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class ScannerResearchedHttpTests(ApiFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnreadableNumberReturnsPhotoCorrectedPendingIdentityAndOnlySourcedPrice(bool priced)
    {
        using var boundary = new Boundary(priced);
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICardRecognitionProvider>(); services.RemoveAll<ICardEvidenceExtractor>();
            services.AddSingleton<ICardEvidenceExtractor>(_ => new OpenAiCardEvidenceExtractor(
                new HttpClient(boundary, false) { BaseAddress = new("https://api.openai.com/v1/") },
                Options.Create(new OpenAiScannerOptions { Enabled = true, ApiKey = "fake", RetryCount = 0 }), NullLogger<OpenAiCardEvidenceExtractor>.Instance));
            services.RemoveAll<ICardEvidenceCatalogEnricher>();
            services.AddScoped<ICardEvidenceCatalogEnricher>(sp => new TcgDexScannerCatalogEnricher(new HttpClient(new MissingCatalog()) { BaseAddress = new("https://catalog.example/") },
                sp.GetRequiredService<ICatalogDiscoveryImporter>(), NullLogger<TcgDexScannerCatalogEnricher>.Instance));
            services.AddScoped<ICardRecognitionProvider>(sp => new EvidenceCardRecognitionProvider(sp.GetRequiredService<ICardEvidenceExtractor>(),
                sp.GetRequiredService<CardEvidenceCatalogMatcher>(), null, "openai", null, enricher: sp.GetRequiredService<ICardEvidenceCatalogEnricher>()));
            services.RemoveAll<IScannerWebMarketResearchProvider>();
            services.AddSingleton<IScannerWebMarketResearchProvider>(sp => new OpenAiScannerWebMarketResearchProvider(
                new HttpClient(boundary, false) { BaseAddress = new("https://api.openai.com/v1/") },
                Options.Create(new OpenAiScannerOptions { Enabled = true, ApiKey = "fake" }), new NoRates(), sp.GetRequiredService<IClock>()));
        }));
        using var client = factory.CreateClient();
        var register = new RegisterRequest($"{Guid.NewGuid():N}@example.test", "Secure-Test-Password1!", "s" + Guid.NewGuid().ToString("N")[..20], "Collector");
        (await client.PostAsJsonAsync("/api/v1/auth/register", register)).EnsureSuccessStatusCode();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(register.Email, register.Password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken);
        using var photo = new Image<Rgba32>(200, 300); using var bytes = new MemoryStream(); photo.SaveAsPng(bytes);
        for (var i = 0; i < 2; i++)
        {
            // An identical-photo retry remains unreadable and must use only its exact capture receipt.
            boundary.NumberReadable = false;
            using var form = new MultipartFormDataContent(); var content = new ByteArrayContent(bytes.ToArray());
            content.Headers.ContentType = new("image/png"); form.Add(content, "image", "card.png");
            using var response = await client.PostAsync("/api/v1/scanner/identify?gameCode=pokemon", form);
            response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync<CardScanResultDto>())!;
            Assert.Empty(result.Candidates);
            Assert.Equal(boundary.Name, result.VisualIdentification!.Name);
            Assert.Equal("067/086", result.VisualIdentification.CollectorNumber);
            Assert.Equal("en", result.VisualIdentification.Language);
            if (priced)
            {
                Assert.Equal(20m, result.MarketEstimate!.AmountBrl);
                Assert.Equal("https://market.example/card", Assert.Single(result.MarketEstimate.Sources).Url);
                Assert.True(result.MarketEstimate.IsEstimate);
                Assert.NotNull(result.MarketEstimate.NextRefreshAt);
            }
            else Assert.Null(result.MarketEstimate);
        }
        Assert.Equal(2, boundary.VisionCalls); Assert.Equal(1, boundary.WebCalls);
    }

    private sealed class Boundary(bool priced) : HttpMessageHandler
    {
        public string Name { get; } = "Web HTTP " + Guid.NewGuid().ToString("N");
        public bool NumberReadable; public int VisionCalls; public int WebCalls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (input.RootElement.TryGetProperty("tools", out _))
            {
                Interlocked.Increment(ref WebCalls);
                var payload = new { name = Name, number = "067/086", language = "en", set = "HTTP set", game = "pokemon", finish = "normal", condition = (string?)null,
                    company = (string?)null, grade = (string?)null, confidence = .92, photoSupported = true, conflict = false,
                    identitySourceUrls = new[] { "https://market.example/card" },
                    sources = priced ? new[] { new { url = "https://market.example/card", title = "Card", amount = 20m, currency = "BRL", basis = "asking",
                        date = DateTimeOffset.UtcNow.ToString("O"), name = Name, game = "pokemon", number = "067/086", language = "en", finish = "normal", set = "HTTP set",
                        company = (string?)null, grade = (string?)null, condition = (string?)null } } : [] };
                object[] output = [new { type = "web_search_call", status = "completed", action = new { type = "search", sources = new[] { new { url = "https://market.example/card" } } } },
                    new { type = "message", content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(payload) } } }];
                return Json(new { status = "completed", output });
            }
            Interlocked.Increment(ref VisionCalls);
            var evidence = new Dictionary<string, object?> { ["schemaVersion"] = 4 };
            foreach (var field in new[] { "gameCode", "name", "collectorNumber", "setCode", "setName", "language", "variant", "hp", "finish", "condition", "isGraded", "gradingCompany", "grade", "certificationNumber", "rarity", "year", "cardType", "stage" })
                evidence[field] = new { value = (string?)null, confidence = 0 };
            foreach (var pair in new Dictionary<string, string> { ["gameCode"] = "pokemon", ["name"] = Name, ["setName"] = "HTTP set", ["language"] = "en", ["variant"] = "normal", ["finish"] = "normal" })
                evidence[pair.Key] = new { value = pair.Value, confidence = .92 };
            if (NumberReadable) evidence["collectorNumber"] = new { value = "067/086", confidence = .92 };
            return Json(new { status = "completed", output = new[] { new { type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(evidence) } } } } });
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
    private sealed class MissingCatalog : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }); }
    private sealed class NoRates : IBrlExchangeRateProvider
    { public Task<BrlExchangeRate?> GetAsync(string currency, CancellationToken ct) => throw new InvalidOperationException("BRL needs no external FX"); }
}
