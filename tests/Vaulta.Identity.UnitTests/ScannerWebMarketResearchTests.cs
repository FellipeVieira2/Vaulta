using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure.MarketResearch;
using Vaulta.Catalog.Infrastructure.Recognition;
using Xunit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.UnitTests;

public sealed class ScannerWebMarketResearchTests
{
    internal static readonly CardVisualIdentificationDto Card = new("Sliggoo", "067/086", "pt-BR", "Example", .9, "pokemon", Finish: "normal");
    [Theory]
    [InlineData(true, "BRL", 10)]
    [InlineData(true, "USD", 50)]
    [InlineData(false, "BRL", 0)]
    [InlineData(true, "ZZZ", 0)]
    public async Task RequiresActualWebSourcesAndTrustedCurrency(bool web, string currency, decimal expected)
    {
        using var provider = Provider(Response(web, currency));
        var result = await provider.ResearchAsync(Card, null, default);
        if (expected == 0) Assert.Null(result?.Estimate); else Assert.Equal(expected, result!.Estimate!.AmountBrl);
    }
    [Theory]
    [InlineData("062/066", "pt-BR", "normal", null)]
    [InlineData("067/086", "en", "normal", null)]
    [InlineData("067/086", "pt-BR", "reverse", null)]
    [InlineData("067/086", "pt-BR", "normal", "PSA")]
    public async Task RejectsConflictingIdentity(string number, string language, string finish, string? company)
    {
        using var provider = Provider(Response(true, "BRL", number, language, finish, company));
        Assert.Null(await provider.ResearchAsync(Card, null, default));
    }
    [Fact]
    public async Task DoesNotShareCertificationSerialAndUsesBoundedWebSchema()
    {
        var card = Card with { Certification = new("PSA", "10", "private-serial") };
        using var handler = new Handler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.DoesNotContain("private-serial", body);
            using var doc = JsonDocument.Parse(body);
            Assert.False(doc.RootElement.GetProperty("store").GetBoolean());
            Assert.Equal(3, doc.RootElement.GetProperty("max_tool_calls").GetInt32());
            Assert.Equal("web_search", doc.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
            Assert.True(doc.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            return Response(true, "BRL", company: "PSA");
        });
        using var provider = new OpenAiScannerWebMarketResearchProvider(new(handler) { BaseAddress = new("https://api.openai.com/v1/") }, Options.Create(new OpenAiScannerOptions { Enabled = true, ApiKey = "test" }), new Fx());
        var result = await provider.ResearchAsync(card, null, default);
        Assert.NotNull(result);
        Assert.Null(result.Identification.Certification!.Number);
    }
    [Fact]
    public async Task MissingNumberNeedsPhotoEvidenceForHighConfidence()
    {
        using var provider = Provider(Response(true, "BRL"));
        Assert.Null(await provider.ResearchAsync(Card with { CollectorNumber = null }, null, default));
    }
    [Fact]
    public async Task VerifiedIdentityWithoutPriceIsPreservedWithoutZeroQuote()
    {
        using var response = Response(true, "BRL");
        var raw = await response.Content.ReadAsStringAsync();
        using var envelope = JsonDocument.Parse(raw);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(envelope.RootElement.GetProperty("output")[1].GetProperty("content")[0].GetProperty("text").GetString()!)!;
        payload["sources"] = new System.Text.Json.Nodes.JsonArray();
        var body = System.Text.Json.Nodes.JsonNode.Parse(raw)!;
        body["output"]![1]!["content"]![0]!["text"] = payload.ToJsonString();
        using var provider = Provider(new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString()) });
        var result = await provider.ResearchAsync(Card, null, default);
        Assert.NotNull(result);
        Assert.Null(result.Estimate);
        Assert.Equal("067/086", result.Identification.CollectorNumber);
    }
    [Theory]
    [InlineData("missing-url")]
    [InlineData("source-printing")]
    [InlineData("conflict")]
    [InlineData("future-price")]
    [InlineData("negative-price")]
    [InlineData("nan-confidence")]
    public async Task RejectsUnverifiedOrConflictingSourceEvidence(string mutation)
    {
        using var response = await Mutate(p =>
        {
            if (mutation == "missing-url") p["sources"]![0]!["url"] = "https://invented.example/card";
            if (mutation == "source-printing") p["sources"]![0]!["number"] = "062/066";
            if (mutation == "conflict") p["conflict"] = true;
            if (mutation == "future-price") p["sources"]![0]!["date"] = DateTimeOffset.UtcNow.AddDays(2).ToString("O");
            if (mutation == "negative-price") p["sources"]![0]!["amount"] = -1;
            if (mutation == "nan-confidence") p["confidence"] = 2;
        });
        using var provider = Provider(response);
        Assert.Null(await provider.ResearchAsync(Card, null, default));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("062/066")]
    public async Task PhotoAndFetchedEvidenceCanResolveNumberWithoutQuote(string? initialNumber)
    {
        using var response = await Mutate(p => { p["photoSupported"] = true; p["sources"] = new System.Text.Json.Nodes.JsonArray(); });
        using var provider = Provider(response);
        using var photo = new Image<Rgba32>(128, 128);
        using var stream = new MemoryStream();
        photo.SaveAsJpeg(stream);
        var result = await provider.ResearchAsync(Card with { CollectorNumber = initialNumber }, stream.ToArray(), default);
        Assert.Equal("067/086", result!.Identification.CollectorNumber);
        Assert.Null(result.Estimate);
    }
    [Fact]
    public async Task ReviewThresholdIsPreservedForSourcedLowConfidenceEstimate()
    {
        using var response = await Mutate(p => p["confidence"] = .79);
        using var provider = Provider(response);
        Assert.Equal(.79, (await provider.ResearchAsync(Card, null, default))!.Estimate!.Confidence);
    }
    [Fact]
    public async Task BackendAveragesIndependentSourcesInsteadOfModelTotal()
    {
        using var response = await Mutate(p =>
        {
            var source = p["sources"]![0]!.DeepClone();
            source["url"] = "https://market.example/second"; source["amount"] = 20;
            p["sources"]!.AsArray().Add(source);
        }, secondUrl: true);
        using var provider = Provider(response);
        Assert.Equal(15m, (await provider.ResearchAsync(Card, null, default))!.Estimate!.AmountBrl);
    }
    internal static async Task<HttpResponseMessage> Mutate(Action<System.Text.Json.Nodes.JsonNode> mutation, bool secondUrl = false)
    {
        using var response = Response(true, "BRL");
        var body = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var payload = System.Text.Json.Nodes.JsonNode.Parse(body["output"]![1]!["content"]![0]!["text"]!.GetValue<string>())!;
        mutation(payload);
        body["output"]![1]!["content"]![0]!["text"] = payload.ToJsonString();
        if (secondUrl) body["output"]![0]!["action"]!["sources"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["url"] = "https://market.example/second" });
        return new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString()) };
    }
    [Fact]
    public async Task UndatedFetchedListingRecordsObservationWithoutInventingPriceDate()
    {
        using var response = await Mutate(p => p["sources"]![0]!["date"] = null);
        var clock = new FixedClock();
        using var provider = new OpenAiScannerWebMarketResearchProvider(new(new Handler(_ => Task.FromResult(response))) { BaseAddress = new("https://api.openai.com/v1/") },
            Options.Create(new OpenAiScannerOptions { Enabled = true, ApiKey = "test" }), new Fx(), clock);
        var estimate = (await provider.ResearchAsync(Card, null, default))!.Estimate!;
        Assert.Equal(clock.UtcNow, estimate.CheckedAt);
        Assert.Null(estimate.Sources.Single().PriceUpdatedAt);
        Assert.Equal("asking", estimate.Sources.Single().Basis);
    }
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow { get; } = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero); }
    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("network")]
    public async Task CorrectedPhotoIdentitySurvivesUnavailableExchangeRate(string mode)
    {
        using var response = await Mutate(p => { p["photoSupported"] = true; p["sources"]![0]!["currency"] = "USD"; });
        using var photo = new Image<Rgba32>(128, 128);
        using var stream = new MemoryStream(); photo.SaveAsJpeg(stream);
        using var provider = new OpenAiScannerWebMarketResearchProvider(new(new Handler(_ => Task.FromResult(response))) { BaseAddress = new("https://api.openai.com/v1/") },
            Options.Create(new OpenAiScannerOptions { Enabled = true, ApiKey = "test" }), new UnavailableFx(mode));
        var result = await provider.ResearchAsync(Card with { CollectorNumber = "062/066" }, stream.ToArray(), default);
        Assert.NotNull(result);
        Assert.Equal("067/086", result.Identification.CollectorNumber);
        Assert.Null(result.Estimate);
        Assert.Equal("exchange_rate_unavailable", result.Issue);
    }
    private sealed class UnavailableFx(string mode) : IBrlExchangeRateProvider
    {
        public Task<BrlExchangeRate?> GetAsync(string currency, CancellationToken ct) => mode switch
        {
            "network" => throw new HttpRequestException("provider unavailable"),
            "stale" => Task.FromResult<BrlExchangeRate?>(new(currency, 5, DateTimeOffset.UtcNow.AddDays(-20))),
            _ => Task.FromResult<BrlExchangeRate?>(null)
        };
    }
    [Fact]
    public async Task UnsupportedGifIsRejectedBeforeWebCall()
    {
        using var photo = new Image<Rgba32>(128, 128);
        using var stream = new MemoryStream(); photo.SaveAsGif(stream);
        var calls = 0;
        using var provider = new OpenAiScannerWebMarketResearchProvider(new(new Handler(_ => { calls++; return Task.FromResult(Response(true, "BRL")); })) { BaseAddress = new("https://api.openai.com/v1/") },
            Options.Create(new OpenAiScannerOptions { Enabled = true, ApiKey = "test" }), new Fx());
        Assert.Null(await provider.ResearchAsync(Card, stream.ToArray(), default));
        Assert.Equal(0, calls);
    }
    [Fact]
    public async Task PhotoMetadataIsRemovedFromForwardedImage()
    {
        using var photo = new Image<Rgba32>(128, 128);
        photo.Metadata.ExifProfile = new();
        photo.Metadata.ExifProfile.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.ImageDescription, "private-location");
        using var stream = new MemoryStream(); photo.SaveAsJpeg(stream);
        using var provider = new OpenAiScannerWebMarketResearchProvider(new(new Handler(async request =>
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var url = doc.RootElement.GetProperty("input")[0].GetProperty("content")[1].GetProperty("image_url").GetString()!;
            using var forwarded = Image.Load(Convert.FromBase64String(url.Split(',')[1]));
            Assert.Null(forwarded.Metadata.ExifProfile);
            return Response(true, "BRL");
        })) { BaseAddress = new("https://api.openai.com/v1/") }, Options.Create(new OpenAiScannerOptions { Enabled = true, ApiKey = "test" }), new Fx());
        Assert.NotNull(await provider.ResearchAsync(Card, stream.ToArray(), default));
    }
    [Fact]
    public async Task MissingVariantPreservesVerifiedIdentityWithoutPricing()
    {
        using var response = await Mutate(p => { p["sources"] = new System.Text.Json.Nodes.JsonArray(); p["finish"] = null; });
        using var provider = Provider(response);
        var result = await provider.ResearchAsync(Card with { Finish = null }, null, default);
        Assert.NotNull(result);
        Assert.Null(result.Estimate);
    }
    [Fact]
    public async Task CapacityDoesNotQueueUnboundedRequests()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async _ => { started.SetResult(); await release.Task; return Response(true, "BRL"); });
        using var provider = new OpenAiScannerWebMarketResearchProvider(new(handler) { BaseAddress = new("https://api.openai.com/v1/") },
            Options.Create(new OpenAiScannerOptions { Enabled = true, ApiKey = "test" }), new Fx(), researchOptions: Options.Create(new ScannerMarketResearchOptions { MaxConcurrency = 1 }));
        var first = provider.ResearchAsync(Card, null, default);
        await started.Task;
        Assert.Null(await provider.ResearchAsync(Card, null, default));
        release.SetResult();
        Assert.NotNull(await first);
    }
    [Fact]
    public async Task OversizedResponseIsRejected()
    {
        using var provider = Provider(new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 131073)) });
        Assert.Null(await provider.ResearchAsync(Card, null, default));
    }
    internal static HttpResponseMessage Response(bool web, string currency, string number = "067/086", string language = "pt-BR", string finish = "normal", string? company = null)
    {
        var payload = new { name = "Sliggoo", number, language, set = "Example", game = "pokemon", finish, condition = (string?)null, company, grade = company == null ? null : "10", confidence = .9, photoSupported = false, conflict = false, identitySourceUrls = new[] { "https://market.example/card" }, sources = new[] { new { url = "https://market.example/card", title = "Card", amount = 10m, currency, basis = "asking", date = DateTimeOffset.UtcNow.ToString("O"), name = "Sliggoo", game = "pokemon", number, language, finish, set = "Example", company, grade = company == null ? null : "10", condition = (string?)null } } };
        var output = new List<object>();
        if (web) output.Add(new { type = "web_search_call", status = "completed", action = new { type = "search", sources = new[] { new { url = "https://market.example/card" } } } });
        output.Add(new { type = "message", content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(payload) } } });
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { status = "completed", output })) };
    }
    private static OpenAiScannerWebMarketResearchProvider Provider(HttpResponseMessage response) => new(new(new Handler(_ => Task.FromResult(response))) { BaseAddress = new("https://api.openai.com/v1/") }, Options.Create(new OpenAiScannerOptions { Enabled = true, ApiKey = "test" }), new Fx());
    internal sealed class Fx : IBrlExchangeRateProvider { public Task<BrlExchangeRate?> GetAsync(string currency, CancellationToken ct) => Task.FromResult<BrlExchangeRate?>(currency == "USD" ? new("USD", 5, DateTimeOffset.UtcNow) : null); }
    internal sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
}

