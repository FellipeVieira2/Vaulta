using Amazon.BedrockRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Catalog.Infrastructure.Recognition;
using Xunit;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.UnitTests;

public sealed class ScannerProviderSelectionTests
{
    [Theory]
    [InlineData("openai", "ocr", false)]
    [InlineData("ocr", null, false)]
    [InlineData("openai", "nova", true)]
    [InlineData("nova", "ocr", true)]
    [InlineData("ocr", "openai", false)]
    public void ExactlyOneRecognitionProviderAndOnlyExplicitNovaCreatesAwsClient(string primary, string? fallback, bool aws)
    {
        var services = Services(new() { ["Scanner:Recognition:Provider"] = primary, ["Scanner:Recognition:FallbackProvider"] = fallback,
            ["Scanner:OpenAI:Enabled"] = "true", ["Scanner:Nova:Enabled"] = "true" });
        Assert.Equal(aws, services.Any(x => x.ServiceType == typeof(IAmazonBedrockRuntime)));
        services.AddSingleton<ICardEvidenceExtractor, EmptyExtractor>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var selected = Assert.Single(scope.ServiceProvider.GetServices<ICardRecognitionProvider>());
        Assert.Equal("pokemon", selected.GameCode);
        Assert.Equal(primary == "ocr" && fallback is null, selected is PokemonTcgRecognitionProvider);
    }

    [Theory]
    [InlineData("Provider", "typo")]
    [InlineData("FallbackProvider", "typo")]
    public void InvalidProviderFailsConfigurationValidation(string key, string value)
    {
        var services = Services(new() { ["Scanner:Recognition:Provider"] = "openai", [$"Scanner:Recognition:{key}"] = value });
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ScannerRecognitionOptions>>().Value);
    }

    [Theory]
    [InlineData("RetryCount", "5")]
    [InlineData("TimeoutSeconds", "0")]
    [InlineData("MaxConcurrency", "5")]
    [InlineData("ImageDetail", "cheap")]
    [InlineData("MaxImageEdge", "32")]
    [InlineData("MaxOutputTokens", "99999")]
    public void OpenAiBoundsAreValidatedAtStartup(string setting, string value)
    {
        using var provider = Services(new() { ["Scanner:Recognition:Provider"] = "openai", [$"Scanner:OpenAI:{setting}"] = value }).BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OpenAiScannerOptions>>().Value);
    }

    [Fact]
    public async Task OpenAiSuccessUsesRealMatcherAndOcrIsNotCalled()
    {
        using var handler = new OpenAiScannerTests.Handler((_, _) => Task.FromResult(OpenAiScannerTests.Response()));
        using var extractor = new OpenAiCardEvidenceExtractor(new HttpClient(handler) { BaseAddress = new("https://api.openai.com/v1/") },
            Options.Create(new OpenAiScannerOptions { Enabled = true, ApiKey = "server-test-key" }), Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAiCardEvidenceExtractor>.Instance);
        var fallback = new Fallback(); var id = Guid.NewGuid();
        var recognition = new EvidenceCardRecognitionProvider(extractor, new(new Catalog(id)), fallback, "openai", "ocr");
        var result = Assert.Single(await recognition.IdentifyAsync(OpenAiScannerTests.ImageBytes(), default));
        Assert.Equal(id.ToString(), result.PrintingId);
        Assert.Null(result.EstimatedMarketValueBrl);
        Assert.Equal(0, fallback.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingEvidenceOrNoMatchFallsBackOnlyOnce(bool evidence)
    {
        var fallback = new Fallback();
        var recognition = new EvidenceCardRecognitionProvider(new Extractor(evidence), new(new Catalog(null)), fallback, "openai", "ocr");
        Assert.Empty(await recognition.IdentifyAsync([1], default));
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task OcrPrimaryEmitsComparableOperationalTrace()
    {
        using var parent = new Activity("ocr-test").Start();
        var traces = new System.Collections.Concurrent.ConcurrentQueue<Activity>();
        using var listener = new ActivityListener { ShouldListenTo = source => source.Name == "Vaulta.Scanner",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { if (activity.ParentId == parent.Id) traces.Enqueue(activity); } };
        ActivitySource.AddActivityListener(listener);
        using var client = new HttpClient();
        var ocr = new PokemonTcgRecognitionProvider(client, new Ocr(), new(new Catalog(null), NullLogger<FuzzyCardSearchService>.Instance), NullLogger<PokemonTcgRecognitionProvider>.Instance);
        Assert.Empty(await ocr.IdentifyAsync([1], default));
        var trace = Assert.Single(traces);
        Assert.Equal("ocr", trace.GetTagItem("scanner.provider"));
        Assert.Equal("no_match", trace.GetTagItem("scanner.outcome"));
        Assert.DoesNotContain(trace.TagObjects, x => x.Value?.ToString()?.Contains("Pikachu") == true);
    }

    private sealed class Ocr : IOcrService
    { public Task<OcrResult> ExtractTextAsync(byte[] bytes, CancellationToken ct) => Task.FromResult(new OcrResult("Pikachu\n058/102", [])); }

    private static IServiceCollection Services(Dictionary<string, string?> values)
    {
        values["ConnectionStrings:Vaulta"] = "Host=localhost;Database=test;Username=test;Password=test";
        return new ServiceCollection().AddSingleton<IClock, Clock>().AddLogging().AddCatalogModule(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class EmptyExtractor : ICardEvidenceExtractor
    { public Task<CardEvidence?> ExtractAsync(byte[] bytes, CancellationToken ct) => Task.FromResult<CardEvidence?>(null); }
    private sealed class Extractor(bool evidence) : ICardEvidenceExtractor
    { public Task<CardEvidence?> ExtractAsync(byte[] bytes, CancellationToken ct) => Task.FromResult(evidence ? CardEvidenceTests.Evidence() : null); }
    private sealed class Catalog(Guid? id) : ICardRecognitionCatalog
    { public Task<IReadOnlyList<RecognitionCatalogCard>> FindCandidatesAsync(string name, string? number, string game, CancellationToken ct) => Task.FromResult<IReadOnlyList<RecognitionCatalogCard>>(id is { } guid ? [CardEvidenceTests.Card(guid, "Pikachu", "58/102", "en")] : []); }
    private sealed class Fallback : ICardRecognitionProvider
    {
        public string GameCode => "pokemon";
        public int Calls { get; private set; }
        public Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] bytes, CancellationToken ct) { Calls++; return Task.FromResult<IReadOnlyList<CardRecognitionCandidate>>([]); }
    }
}
