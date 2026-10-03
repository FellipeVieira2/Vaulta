using System.Net;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Infrastructure.MarketResearch;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class JustTcgScannerMarketResearchTests
{
    [Theory]
    [InlineData("Portuguese", "Normal", "067/086", 50)]
    [InlineData("Portuguese", "Normal", "67/86", 50)]
    [InlineData("English", "Normal", "067/086", 0)]
    [InlineData(null, "Normal", "067/086", 0)]
    [InlineData("Portuguese", "Reverse Holofoil", "067/086", 0)]
    [InlineData("Portuguese", "Normal", "062/066", 0)]
    public async Task FetchesOnlyExactLanguagePrintingAndNumber(string? language, string printing, string number, decimal expected)
    {
        using var handler = new ScannerWebMarketResearchTests.Handler(request =>
        {
            Assert.Equal("server-test", request.Headers.GetValues("x-api-key").Single());
            Assert.Contains("/v1/cards?", request.RequestUri!.AbsoluteUri);
            Assert.Contains("language=Portuguese", request.RequestUri.Query);
            var data = new { data = new[] { new { id = "pokemon-example-sliggoo", name = "Sliggoo", game = "pokemon", set_name = "Example", number, variants = new[] { new { id = "v1", printing, language, condition = "Near Mint", price = 10m, lastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds() } } } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(data)) });
        });
        var provider = new JustTcgScannerMarketResearchProvider(new(handler) { BaseAddress = new("https://api.justtcg.com/") }, Options.Create(new JustTcgScannerOptions { ApiKey = "server-test" }), new ScannerWebMarketResearchTests.Fx());
        var card = ScannerWebMarketResearchTests.Card with { Condition = "near-mint" };
        var result = await provider.ResearchAsync(card, null, default);
        if (expected == 0) Assert.Null(result); else
        {
            Assert.Equal(expected, result!.Estimate!.AmountBrl);
            Assert.Contains("North America", result.Estimate.Source);
        }
    }
    [Fact]
    public async Task GradedCardCannotReceiveRawPrice()
    {
        using var handler = new ScannerWebMarketResearchTests.Handler(_ => throw new Exception("must not fetch raw for graded"));
        var provider = new JustTcgScannerMarketResearchProvider(new(handler), Options.Create(new JustTcgScannerOptions { ApiKey = "server-test" }), new ScannerWebMarketResearchTests.Fx());
        Assert.Null(await provider.ResearchAsync(ScannerWebMarketResearchTests.Card with { Certification = new("PSA", "10", "serial") }, null, default));
    }
    [Theory]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task CompositeFallsBackToWebAfterJustTcgUnavailable(int status)
    {
        using var directHandler = new ScannerWebMarketResearchTests.Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var webHandler = new ScannerWebMarketResearchTests.Handler(_ => Task.FromResult(ScannerWebMarketResearchTests.Response(true, "BRL")));
        using var direct = new JustTcgScannerMarketResearchProvider(new(directHandler) { BaseAddress = new("https://api.justtcg.com/") },
            Options.Create(new JustTcgScannerOptions { ApiKey = "test" }), new ScannerWebMarketResearchTests.Fx());
        using var web = new OpenAiScannerWebMarketResearchProvider(new(webHandler) { BaseAddress = new("https://api.openai.com/v1/") },
            Options.Create(new Vaulta.Catalog.Infrastructure.Recognition.OpenAiScannerOptions { Enabled = true, ApiKey = "test" }), new ScannerWebMarketResearchTests.Fx());
        var composite = new CompositeScannerWebMarketResearchProvider(direct, web);
        Assert.Equal(10m, (await composite.ResearchAsync(ScannerWebMarketResearchTests.Card, null, default))!.Estimate!.AmountBrl);
    }
    [Theory]
    [InlineData("pokemon", "pokemon", "Pokemon")]
    [InlineData("yugioh", "yu-gi-oh", "Yu-Gi-Oh!")]
    [InlineData("onepiece", "one-piece-card-game", "One Piece Card Game")]
    public async Task UsesOfficialGameIdentifiers(string inputGame, string providerGame, string responseGame)
    {
        using var handler = new ScannerWebMarketResearchTests.Handler(request =>
        {
            Assert.Contains($"game={providerGame}", request.RequestUri!.Query);
            var body = new { data = new[] { new { id = "example", name = "Sliggoo", game = responseGame, set_name = "Example", number = "067/086", variants = new[] { new { id = "v1", printing = "Normal", language = "Portuguese", condition = "Near Mint", price = 10, lastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds() } } } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body)) });
        });
        using var provider = new JustTcgScannerMarketResearchProvider(new(handler) { BaseAddress = new("https://api.justtcg.com/") }, Options.Create(new JustTcgScannerOptions { ApiKey = "test" }), new ScannerWebMarketResearchTests.Fx());
        Assert.NotNull((await provider.ResearchAsync(ScannerWebMarketResearchTests.Card with { GameCode = inputGame }, null, default))?.Estimate);
    }
}
