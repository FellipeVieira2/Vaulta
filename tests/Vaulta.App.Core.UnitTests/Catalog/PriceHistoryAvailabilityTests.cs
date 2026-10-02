using Vaulta.App.Core.Catalog;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class PriceHistoryAvailabilityTests
{
    [Theory]
    [InlineData("portfolio", null)]
    [InlineData("card", null)]
    [InlineData("filter", null)]
    [InlineData("portfolio", "test-key")]
    [InlineData("card", "test-key")]
    [InlineData("filter", "test-key")]
    public void MissingVerifiedBackendHistoryCannotBecomeSimulatedMarketData(string history, string? apiKey)
    {
        using var client = new HttpClient(new NoNetwork());
        var provider = new JustTcgPriceHistoryProvider(client, new SimulatedPriceHistoryProvider(), apiKey);
        var points = history switch
        {
            "portfolio" => provider.GetPortfolioHistory(),
            "card" => provider.GetCardPriceHistory(Guid.NewGuid()),
            _ => provider.GetFilterPriceHistory("pokemon")
        };
        Assert.Empty(points);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("test-key")]
    public async Task LegacyVariantLookupWithoutTrustedMappingReturnsUnavailableAndSendsNoProprietaryKey(string? apiKey)
    {
        using var client = new HttpClient(new NoNetwork()) { BaseAddress = new Uri("https://example.invalid/") };
        var provider = new JustTcgPriceHistoryProvider(client, new SimulatedPriceHistoryProvider(), apiKey);
        Assert.False(client.DefaultRequestHeaders.Contains("x-api-key"));
        var result = await provider.GetVariantPriceHistoryAsync(Guid.NewGuid().ToString());
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task UnavailableVariantHistoryStillPropagatesCallerCancellation()
    {
        using var client = new HttpClient(new NoNetwork());
        var provider = new JustTcgPriceHistoryProvider(client, new SimulatedPriceHistoryProvider());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetVariantPriceHistoryAsync("missing", ct: cancellation.Token));
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Price history must be supplied by a trusted backend; no direct market API call is authorized.");
    }
}
