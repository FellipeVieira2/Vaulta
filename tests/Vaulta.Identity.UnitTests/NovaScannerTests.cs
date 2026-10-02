using System.Net;
using System.Diagnostics;
using System.Collections.Concurrent;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure.Recognition;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class NovaScannerTests
{
    [Fact]
    public async Task Extractor_UsesBoundedConverseImageRequestAndValidatesResponse()
    {
        var image = ImageBytes();
        using var client = new BedrockClient((request, _) =>
        {
            Assert.Equal("amazon.nova-lite-v1:0", request.ModelId);
            Assert.Equal(768, request.InferenceConfig.MaxTokens);
            Assert.Equal(0, request.InferenceConfig.Temperature);
            var message = Assert.Single(request.Messages);
            Assert.Equal("user", message.Role.Value);
            var payload = Assert.Single(message.Content, x => x.Image is not null).Image;
            Assert.Equal("png", payload.Format.Value);
            Assert.Equal(image, payload.Source.Bytes.ToArray());
            Assert.DoesNotContain("printingId", string.Join(' ', request.System.Select(x => x.Text)), StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(Response(CardEvidenceTests.ValidJson));
        });
        var result = Assert.IsType<CardEvidence>(await Extractor(client).ExtractAsync(image, default));
        Assert.Equal("Pikachu", result.Name.Value);
        Assert.Equal("amazon.nova-lite-v1:0", result.ModelVersion);
        Assert.Equal("card-evidence-v1", result.PromptVersion);
    }

    [Theory]
    [InlineData("not-json", "end_turn")]
    [InlineData(CardEvidenceTests.ValidJson, "max_tokens")]
    [InlineData(CardEvidenceTests.ValidJson, "tool_use")]
    public async Task Extractor_InvalidOrTruncatedResponseCannotBecomeEvidence(string json, string stopReason)
    {
        using var client = new BedrockClient((_, _) => Task.FromResult(Response(json, stopReason)));
        Assert.Null(await Extractor(client).ExtractAsync(ImageBytes(), default));
    }

    [Fact]
    public async Task Extractor_RejectsOversizedAndInvalidImagesBeforeExternalCall()
    {
        using var client = new BedrockClient((_, _) => throw new InvalidOperationException("Invalid image was sent to AWS"));
        Assert.Null(await Extractor(client).ExtractAsync([1, 2, 3], default));
        Assert.Null(await Extractor(client).ExtractAsync(new byte[3_750_001], default));
    }

    [Fact]
    public async Task Extractor_RetriesOnlyTransientErrorsWithinConfiguredBound()
    {
        var calls = 0;
        using var client = new BedrockClient((_, _) =>
        {
            calls++;
            return calls == 1
                ? Task.FromException<ConverseResponse>(new AmazonBedrockRuntimeException("unlogged sensitive response") { StatusCode = HttpStatusCode.TooManyRequests })
                : Task.FromResult(Response(CardEvidenceTests.ValidJson));
        });
        Assert.NotNull(await Extractor(client, new() { RetryCount = 1, RetryDelayMilliseconds = 0 }).ExtractAsync(ImageBytes(), default));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, 2)]
    [InlineData(HttpStatusCode.Forbidden, 1)]
    public async Task Extractor_FailingServiceReturnsNoEvidenceWithBoundedAttempts(HttpStatusCode status, int expectedCalls)
    {
        var calls = 0;
        using var client = new BedrockClient((_, _) =>
        {
            calls++;
            return Task.FromException<ConverseResponse>(new AmazonBedrockRuntimeException("private") { StatusCode = status });
        });
        Assert.Null(await Extractor(client, new() { RetryCount = 1, RetryDelayMilliseconds = 0 }).ExtractAsync(ImageBytes(), default));
        Assert.Equal(expectedCalls, calls);
    }

    [Fact]
    public async Task Extractor_TimeoutReturnsNoEvidenceAndCallerCancellationPropagates()
    {
        using var client = new BedrockClient(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Response(CardEvidenceTests.ValidJson);
        });
        var extractor = Extractor(client, new() { TimeoutSeconds = 1 });
        Assert.Null(await extractor.ExtractAsync(ImageBytes(), default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => extractor.ExtractAsync(ImageBytes(), cancelled.Token));
    }

    [Fact]
    public async Task Extractor_ConcurrencyLimitQueuesSecondCallAndReleasesCapacityAfterCancellation()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirst = new TaskCompletionSource<ConverseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new BedrockClient((_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstStarted.TrySetResult();
                return finishFirst.Task.WaitAsync(token);
            }
            secondStarted.TrySetResult();
            return Task.FromResult(Response(CardEvidenceTests.ValidJson));
        });
        using var extractor = Extractor(client, new() { MaxConcurrency = 1 });
        var first = extractor.ExtractAsync(ImageBytes(), default);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancelQueued = new CancellationTokenSource();
        var queued = extractor.ExtractAsync(ImageBytes(), cancelQueued.Token);
        Assert.NotSame(secondStarted.Task, await Task.WhenAny(secondStarted.Task, Task.Delay(50)));
        cancelQueued.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        finishFirst.SetResult(Response(CardEvidenceTests.ValidJson));
        Assert.NotNull(await first);
        Assert.NotNull(await extractor.ExtractAsync(ImageBytes(), default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Provider_MatchedEvidenceUsesCatalogAndDoesNotInvokeFallback()
    {
        var id = Guid.NewGuid();
        var fallback = new Fallback([]);
        var provider = new NovaCardRecognitionProvider(new EvidenceExtractor(CardEvidenceTests.Evidence()),
            new(new Catalog([CardEvidenceTests.Card(id, "Pikachu", "58/102", "en")])), fallback);
        var result = Assert.Single(await provider.IdentifyAsync([1], default));
        Assert.Equal(id.ToString(), result.PrintingId);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Provider_NoUsableEvidenceFallsBackToExistingOcrCandidates()
    {
        var candidate = new CardRecognitionCandidate(Guid.NewGuid().ToString(), "Pikachu", "Base", "58", null, null, null, null, ["normal"], 0.6);
        var fallback = new Fallback([candidate]);
        var provider = new NovaCardRecognitionProvider(new EvidenceExtractor(null), new(new Catalog([])), fallback);
        Assert.Same(candidate, Assert.Single(await provider.IdentifyAsync([1], default)));
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task Provider_ConflictingEvidenceWithoutCatalogMatchFallsBack()
    {
        var fallback = new Fallback([]);
        var provider = new NovaCardRecognitionProvider(new EvidenceExtractor(CardEvidenceTests.Evidence()), new(new Catalog([])), fallback);
        Assert.Empty(await provider.IdentifyAsync([1], default));
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task Provider_EmitsVersionedMatchTraceWithoutPrintedTextOrImage()
    {
        using var parent = new Activity("scanner-test").Start();
        var traces = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Vaulta.Scanner",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { if (activity.ParentId == parent.Id) traces.Enqueue(activity); }
        };
        ActivitySource.AddActivityListener(listener);
        var provider = new NovaCardRecognitionProvider(new EvidenceExtractor(CardEvidenceTests.Evidence()),
            new(new Catalog([CardEvidenceTests.Card(Guid.NewGuid(), "Pikachu", "58/102", "en")])), new Fallback([]));
        await provider.IdentifyAsync([5, 6, 7], default);
        var trace = Assert.Single(traces);
        Assert.Equal("p", trace.GetTagItem("scanner.prompt.version"));
        Assert.Equal("m", trace.GetTagItem("scanner.model.version"));
        Assert.Equal("catalog-evidence-v1", trace.GetTagItem("scanner.matcher.version"));
        Assert.Equal("Matched", trace.GetTagItem("scanner.match.status"));
        Assert.DoesNotContain(trace.TagObjects, tag => tag.Value?.ToString()?.Contains("Pikachu", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(trace.TagObjects, tag => tag.Value is byte[]);
    }

    private static NovaCardEvidenceExtractor Extractor(IAmazonBedrockRuntime client, NovaScannerOptions? options = null) =>
        new(client, Options.Create(options ?? new()), NullLogger<NovaCardEvidenceExtractor>.Instance);

    private static ConverseResponse Response(string json, string stopReason = "end_turn") => new()
    {
        Output = new ConverseOutput { Message = new Message { Role = ConversationRole.Assistant, Content = [new ContentBlock { Text = json }] } },
        StopReason = new StopReason(stopReason), Usage = new TokenUsage { InputTokens = 100, OutputTokens = 200 }, HttpStatusCode = HttpStatusCode.OK
    };

    private static byte[] ImageBytes()
    {
        using var image = new Image<Rgba32>(1, 1);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private sealed class BedrockClient(Func<ConverseRequest, CancellationToken, Task<ConverseResponse>> response) :
        AmazonBedrockRuntimeClient(new AnonymousAWSCredentials(), new AmazonBedrockRuntimeConfig { RegionEndpoint = RegionEndpoint.USEast1, MaxErrorRetry = 0 })
    {
        public override Task<ConverseResponse> ConverseAsync(ConverseRequest request, CancellationToken cancellationToken = default) => response(request, cancellationToken);
    }

    private sealed class EvidenceExtractor(CardEvidence? evidence) : ICardEvidenceExtractor
    {
        public Task<CardEvidence?> ExtractAsync(byte[] imageData, CancellationToken cancellationToken) => Task.FromResult(evidence);
    }

    private sealed class Catalog(IReadOnlyList<RecognitionCatalogCard> cards) : ICardRecognitionCatalog
    {
        public Task<IReadOnlyList<RecognitionCatalogCard>> FindCandidatesAsync(string name, string? collectorNumber, string gameCode, CancellationToken cancellationToken) => Task.FromResult(cards);
    }

    private sealed class Fallback(IReadOnlyList<CardRecognitionCandidate> cards) : ICardRecognitionProvider
    {
        public string GameCode => "pokemon";
        public int Calls { get; private set; }
        public Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] imageData, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(cards);
        }
    }
}
