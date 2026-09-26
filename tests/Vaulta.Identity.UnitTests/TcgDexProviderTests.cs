using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Infrastructure;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class TcgDexProviderTests
{
    [Fact]
    public async Task ReadsBriefsThenDetailsAndNormalizesFlagsArtworkAndLanguage()
    {
        var paths = new ConcurrentBag<string>();
        using var handler = new Handler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            return Task.FromResult(Json(Fixture(path.EndsWith("/sets", StringComparison.Ordinal) ? "sets" : path.Contains("/sets/", StringComparison.Ordinal) ? "set" : "card")));
        });
        using var client = Client(handler);
        var provider = new TcgDexProvider(client, Options.Create(new TcgDexOptions { Language = "EN" }));
        var sets = await provider.GetSets(default);
        Assert.Null(Assert.Single(sets).Code);
        Assert.Null(sets[0].ReleaseDate);
        var set = await provider.GetSetDetails("base1", default);
        Assert.Equal(new DateOnly(1999, 1, 9), set.Set.ReleaseDate);
        var card = Assert.Single(set.Printings);
        Assert.Equal("058", card.CollectorNumber);
        Assert.Equal("Common", card.Rarity);
        Assert.Equal("en", card.Language);
        Assert.Equal("https://assets.tcgdex.net/en/base/base1/58/high.png", card.ImageUrl);
        Assert.Equal(["normal", "reverse"], card.Variants.Select(x => x.Code));
        Assert.Equal(["/v2/en/cards/base1-58", "/v2/en/sets", "/v2/en/sets/base1"], paths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OptionalFieldsAndUnknownTreatmentsAreSupported()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath.Contains("/sets/", StringComparison.Ordinal)
            ? Fixture("set") : """{"id":"base1-58","name":"Pikachu","localId":58,"set":{"id":"base1","name":"Base Set"},"variants":{"firstEdition":true,"futureFoil":true,"normal":false}}""")));
        using var client = Client(handler);
        var provider = new TcgDexProvider(client, Options.Create(new TcgDexOptions { Language = "pt" }));
        var card = Assert.Single((await provider.GetSetDetails("base1", default)).Printings);
        Assert.Null(card.Rarity); Assert.Null(card.ImageUrl);
        Assert.Equal("pt", card.Language); Assert.Equal("58", card.CollectorNumber);
        Assert.Equal(["first-edition", "future-foil"], card.Variants.Select(x => x.Code));
        Assert.Equal("firstEdition", card.Variants[0].RawValue);
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(404, false)]
    public async Task HttpFailuresHaveFiniteRetriesAndClassification(int status, bool transient)
    {
        var calls = 0;
        using var handler = new Handler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.RetryAfter = new(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        using var client = Client(handler);
        var provider = new TcgDexProvider(client, Options.Create(new TcgDexOptions { RetryCount = 2 }));
        var error = await Assert.ThrowsAsync<CatalogProviderException>(() => provider.GetSets(default));
        Assert.Equal(transient, error.IsTransient);
        Assert.Equal($"http_{status}", error.ErrorCategory);
        Assert.Equal(transient ? 3 : 1, calls);
    }

    [Fact]
    public async Task RetryAfterLongerThanBudgetFailsWithoutRetryingEarly()
    {
        var calls = 0;
        using var handler = new Handler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        });
        using var client = Client(handler);
        var provider = new TcgDexProvider(client, Options.Create(new TcgDexOptions()));
        await Assert.ThrowsAsync<CatalogProviderException>(() => provider.GetSets(default));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("{bad-json", "invalid_json")]
    [InlineData("[{\"id\":\"x\"}]", "invalid_contract")]
    public async Task InvalidJsonOrRequiredIdentityIsPermanent(string payload, string category)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(payload)));
        using var client = Client(handler);
        var provider = new TcgDexProvider(client, Options.Create(new TcgDexOptions()));
        var error = await Assert.ThrowsAsync<CatalogProviderException>(() => provider.GetSets(default));
        Assert.False(error.IsTransient); Assert.Equal(category, error.ErrorCategory);
    }

    [Fact]
    public async Task TimeoutIsTransientButCallerCancellationPropagates()
    {
        using var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Json("[]"); });
        using var client = Client(handler);
        var provider = new TcgDexProvider(client, Options.Create(new TcgDexOptions { Timeout = 1, RetryCount = 0 }));
        var error = await Assert.ThrowsAsync<CatalogProviderException>(() => provider.GetSets(default));
        Assert.True(error.IsTransient); Assert.Equal("timeout", error.ErrorCategory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetSets(cancellation.Token));
    }

    [Fact]
    public async Task RequestsAreBoundedAndTransientFailureCanRecover()
    {
        var active = 0; var peak = 0; var attempts = 0;
        var cards = Enumerable.Range(1, 20).Select(i => new { id = $"base1-{i}", name = "Pikachu", localId = i.ToString() });
        using var handler = new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/sets/", StringComparison.Ordinal))
                return Json(JsonSerializer.Serialize(new { id = "base1", name = "Base Set", cards }));
            var count = Interlocked.Increment(ref active);
            int previous;
            do { previous = Volatile.Read(ref peak); } while (count > previous && Interlocked.CompareExchange(ref peak, count, previous) != previous);
            try
            {
                await Task.Delay(10, ct);
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    var retry = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    retry.Headers.RetryAfter = new(TimeSpan.Zero);
                    return retry;
                }
                var id = request.RequestUri.Segments[^1];
                return Json(JsonSerializer.Serialize(new { id, name = "Pikachu", localId = id.Split('-')[1], set = new { id = "base1", name = "Base Set" }, variants = new { normal = true } }));
            }
            finally { Interlocked.Decrement(ref active); }
        });
        using var client = Client(handler);
        var provider = new TcgDexProvider(client, Options.Create(new TcgDexOptions { MaxConcurrency = 4 }));
        Assert.Equal(20, (await provider.GetSetDetails("base1", default)).Printings.Count);
        Assert.InRange(peak, 2, 4); Assert.Equal(21, attempts);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("https://assets.tcgdex.net/en/base/base1/58", "https://assets.tcgdex.net/en/base/base1/58/high.png")]
    [InlineData("https://assets.tcgdex.net/en/base/base1/58/high.webp", "https://assets.tcgdex.net/en/base/base1/58/high.webp")]
    public void ArtworkMatchesDocumentedAssetFormat(string? source, string? expected) => Assert.Equal(expected, TcgDexProvider.ArtworkUrl(source));

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TcgDex", name + ".json"));
    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://api.tcgdex.net/v2/"), Timeout = Timeout.InfiniteTimeSpan };
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
