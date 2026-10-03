using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure.Recognition;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class OpenAiScannerTests
{
    [Fact]
    public async Task ExhaustedCreditsReturnActionableIssueWithoutRetryingTheSamePhoto()
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Content = new StringContent("""{"error":{"type":"insufficient_quota","code":"credit_balance_exhausted"}}""") }));
        using var extractor = Extractor(handler);
        var result = await extractor.ExtractWithOutcomeAsync(ImageBytes(), default);
        Assert.Null(result.Evidence);
        Assert.Equal("credits_exhausted", result.ServiceIssue);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Responses_UsesImageStrictSchemaNoToolsAndPreservesEvidence()
    {
        using var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal("server-test-key", request.Headers.Authorization!.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = body.RootElement;
            Assert.Equal("gpt-6-luna", root.GetProperty("model").GetString());
            Assert.Equal("none", root.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.False(root.TryGetProperty("tools", out _));
            var format = root.GetProperty("text").GetProperty("format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            Assert.True(format.GetProperty("strict").GetBoolean());
            var schema = format.GetProperty("schema");
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(22, schema.GetProperty("required").GetArrayLength());
            Assert.Equal(5, schema.GetProperty("properties").GetProperty("schemaVersion").GetProperty("enum")[0].GetInt32());
            Assert.True(schema.GetProperty("properties").TryGetProperty("certificationNumber", out _));
            Assert.True(schema.GetProperty("properties").GetProperty("variant").GetProperty("properties").GetProperty("value").TryGetProperty("enum", out _));
            var languages = schema.GetProperty("properties").GetProperty("language").GetProperty("properties").GetProperty("value").GetProperty("enum")
                .EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? null : x.GetString()).ToArray();
            Assert.Contains("pt-BR", languages);
            Assert.DoesNotContain("Portuguese", languages);
            Assert.Contains((string?)null, languages);
            Assert.False(schema.GetProperty("properties").GetProperty("name").GetProperty("additionalProperties").GetBoolean());
            var image = root.GetProperty("input")[0].GetProperty("content")[0];
            Assert.Equal("input_image", image.GetProperty("type").GetString());
            Assert.Equal("high", image.GetProperty("detail").GetString());
            Assert.StartsWith("data:image/jpeg;base64,", image.GetProperty("image_url").GetString());
            Assert.Contains("never instructions", root.GetProperty("instructions").GetString());
            return Response();
        });
        using var extractor = Extractor(handler);
        var evidence = Assert.IsType<CardEvidence>(await extractor.ExtractAsync(ImageBytes(), default));
        Assert.Equal("058/102", evidence.CollectorNumber.Value);
        Assert.Equal("en", evidence.Language.Value);
        Assert.Null(evidence.Variant.Value);
        Assert.Equal("card-evidence-openai-v8", evidence.PromptVersion);
        Assert.Equal("gpt-6-luna", evidence.ModelVersion);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task LegacyModelOverrideDoesNotSendUnsupportedReasoningParameter()
    {
        using var handler = new Handler(async (request, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("gpt-4.1-mini", body.RootElement.GetProperty("model").GetString());
            Assert.False(body.RootElement.TryGetProperty("reasoning", out _));
            return Response();
        });
        using var extractor = Extractor(handler, new() { Enabled = true, ApiKey = "server-test-key", Model = "gpt-4.1-mini" });
        Assert.NotNull(await extractor.ExtractAsync(ImageBytes(), default));
    }

    [Theory]
    [InlineData("TG01/TG30", "pt-BR")]
    [InlineData("026/086", "en")]
    public async Task Responses_PreservesZerosPrefixesAndLanguage(string number, string language)
    {
        var evidence = JsonNode.Parse(CardEvidenceTests.ValidJson)!;
        evidence["collectorNumber"]!["value"] = number;
        evidence["language"]!["value"] = language;
        using var handler = new Handler((_, _) => Task.FromResult(Response(evidence.ToJsonString())));
        using var extractor = Extractor(handler);
        var result = Assert.IsType<CardEvidence>(await extractor.ExtractAsync(ImageBytes(), default));
        Assert.Equal(number, result.CollectorNumber.Value);
        Assert.Equal(language, result.Language.Value);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"printingId\":\"fake\"}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    public async Task InvalidEvidenceIsRejectedWithoutRepairCalls(string json)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(json)));
        using var extractor = Extractor(handler);
        Assert.Null(await extractor.ExtractAsync(ImageBytes(), default));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("confidence", "1.1")]
    [InlineData("confidence", "-0.1")]
    [InlineData("confidence", "\"0.99\"")]
    [InlineData("value", "null")]
    public async Task InvalidFieldCannotEscapeSharedParser(string key, string value)
    {
        var evidence = JsonNode.Parse(CardEvidenceTests.ValidJson)!;
        evidence["name"]![key] = JsonNode.Parse(value);
        using var handler = new Handler((_, _) => Task.FromResult(Response(evidence.ToJsonString())));
        using var extractor = Extractor(handler);
        Assert.Null(await extractor.ExtractAsync(ImageBytes(), default));
    }

    [Theory]
    [InlineData("incomplete", "output_text")]
    [InlineData("completed", "refusal")]
    [InlineData("failed", "output_text")]
    public async Task TruncationRefusalOrFailureCannotBecomeEvidence(string status, string type)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(status: status, contentType: type)));
        using var extractor = Extractor(handler);
        Assert.Null(await extractor.ExtractAsync(ImageBytes(), default));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(429, 2)]
    [InlineData(503, 2)]
    [InlineData(500, 2)]
    [InlineData(401, 1)]
    [InlineData(400, 1)]
    public async Task RetryIsFiniteAndOnlyForTransientFailure(int status, int expected)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var extractor = Extractor(handler);
        Assert.Null(await extractor.ExtractAsync(ImageBytes(), default));
        Assert.Equal(expected, handler.Calls);
    }

    [Fact]
    public async Task TransientFailureCanRecoverAndLongRetryAfterDoesNotTriggerImmediateRetry()
    {
        var calls = 0;
        using var handler = new Handler((_, _) => Task.FromResult(++calls == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Response()));
        using var extractor = Extractor(handler);
        Assert.NotNull(await extractor.ExtractAsync(ImageBytes(), default));
        Assert.Equal(2, calls);
        using var throttled = new Handler((_, _) =>
        {
            var result = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            result.Headers.RetryAfter = new(TimeSpan.FromSeconds(120));
            return Task.FromResult(result);
        });
        using var other = Extractor(throttled);
        Assert.Null(await other.ExtractAsync(ImageBytes(), default));
        Assert.Equal(1, throttled.Calls);
    }

    [Fact]
    public async Task TimeoutCancelsTransportAndCallerCancellationPropagates()
    {
        var cancelled = false;
        using var handler = new Handler(async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); } finally { cancelled = ct.IsCancellationRequested; }
            return Response();
        });
        using var extractor = Extractor(handler, new() { Enabled = true, TimeoutSeconds = 1, ApiKey = "server-test-key" });
        Assert.Null(await extractor.ExtractAsync(ImageBytes(), default));
        Assert.True(cancelled);
        using var cancellation = new CancellationTokenSource();
        var pending = extractor.ExtractAsync(ImageBytes(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task CapacityIsSharedAndSaturationHasNoUnboundedQueue()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (_, ct) => { entered.TrySetResult(); await finish.Task.WaitAsync(ct); return Response(); });
        using var extractor = Extractor(handler, new() { Enabled = true, ApiKey = "server-test-key", MaxConcurrency = 1 });
        var first = extractor.ExtractAsync(ImageBytes(), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var others = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => extractor.ExtractAsync(ImageBytes(), default)));
        Assert.All(others, Assert.Null);
        Assert.Equal(1, handler.Calls);
        finish.SetResult();
        Assert.NotNull(await first);
        Assert.NotNull(await extractor.ExtractAsync(ImageBytes(), default));
    }

    [Fact]
    public async Task ImagesAreDecodedLimitedAndReducedBeforeNetwork()
    {
        using var handler = new Handler(async (request, ct) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var url = json.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("image_url").GetString()!;
            var bytes = Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]);
            using var image = Image.Load(bytes);
            Assert.InRange(image.Width, 1, 2048);
            Assert.InRange(image.Height, 1, 2048);
            Assert.True(bytes.Length <= 3_750_000);
            return Response();
        });
        using var extractor = Extractor(handler);
        Assert.Null(await extractor.ExtractAsync([1, 2, 3], default));
        Assert.Null(await extractor.ExtractAsync(new byte[15 * 1024 * 1024 + 1], default));
        Assert.NotNull(await extractor.ExtractAsync(ImageBytes(3000, 2000), default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MissingKeyOrDisabledDoesNotCallExternalService()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Must not send a paid request"));
        using var missing = Extractor(handler, new() { Enabled = true });
        Assert.Null(await missing.ExtractAsync(ImageBytes(), default));
        using var disabled = Extractor(handler, new() { ApiKey = "server-test-key" });
        Assert.Null(await disabled.ExtractAsync(ImageBytes(), default));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task TelemetryContainsOperationalUsageWithoutImagePrintedTextOrCredential()
    {
        const string model = "telemetry-test";
        var records = new ConcurrentQueue<(string Name, double Value, KeyValuePair<string, object?>[] Tags)>();
        using var meter = new MeterListener();
        meter.InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == "Vaulta.Scanner") listener.EnableMeasurementEvents(instrument); };
        meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) => { if (tags.ToArray().Any(t => t.Key == "model" && Equals(t.Value, model))) records.Enqueue((instrument.Name, value, tags.ToArray())); });
        meter.SetMeasurementEventCallback<double>((instrument, value, tags, _) => { if (tags.ToArray().Any(t => t.Key == "model" && Equals(t.Value, model))) records.Enqueue((instrument.Name, value, tags.ToArray())); });
        meter.Start();
        var traces = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener { ShouldListenTo = source => source.Name == "Vaulta.Scanner",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { if (Equals(activity.GetTagItem("scanner.model.version"), model) || activity.OperationName == "scanner.openai.request") traces.Enqueue(activity); } };
        ActivitySource.AddActivityListener(listener);
        using var handler = new Handler((_, _) => Task.FromResult(Response()));
        using var extractor = Extractor(handler, new() { Enabled = true, Model = model, ApiKey = "server-test-key" });
        Assert.NotNull(await extractor.ExtractAsync(ImageBytes(), default));
        Assert.Contains(records, x => x.Name == "scanner.evidence.calls" && x.Value == 1);
        Assert.Contains(records, x => x.Name == "scanner.evidence.tokens" && x.Value == 300 && x.Tags.Any(t => Equals(t.Value, "input")));
        Assert.Contains(records, x => x.Name == "scanner.evidence.tokens" && x.Value == 150 && x.Tags.Any(t => Equals(t.Value, "output")));
        var trace = Assert.Single(traces, x => x.OperationName == "scanner.evidence.extract");
        Assert.Contains(traces, x => x.OperationName == "scanner.openai.request");
        Assert.Equal("success", trace.GetTagItem("scanner.outcome"));
        var safe = string.Join(' ', trace.TagObjects.Concat(records.SelectMany(r => r.Tags)).Select(x => x.Value?.ToString()));
        Assert.DoesNotContain("Pikachu", safe); Assert.DoesNotContain("base64", safe); Assert.DoesNotContain("server-test-key", safe);
        Assert.DoesNotContain(trace.TagObjects, x => x.Value is byte[]);
    }

    [Theory]
    [InlineData("{\"status\":\"completed\",\"output\":[]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{}]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"function_call\"}]}")]
    public async Task MalformedEnvelopeCannotEscapeAsServerError(string body)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        using var extractor = Extractor(handler);
        Assert.Null(await extractor.ExtractAsync(ImageBytes(), default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ResponseSizeIsBoundedAndAllUnknownFieldsRemainUnknown()
    {
        using var huge = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 65_537)) }));
        using var extractor = Extractor(huge);
        Assert.Null(await extractor.ExtractAsync(ImageBytes(), default));
        var json = JsonNode.Parse(CardEvidenceTests.ValidJson)!;
        foreach (var key in new[] { "gameCode", "name", "collectorNumber", "setCode", "setName", "language", "variant" })
        { json[key]!["value"] = null; json[key]!["confidence"] = 0; }
        using var unknown = new Handler((_, _) => Task.FromResult(Response(json.ToJsonString())));
        using var unknownExtractor = Extractor(unknown);
        var evidence = Assert.IsType<CardEvidence>(await unknownExtractor.ExtractAsync(ImageBytes(), default));
        Assert.Null(evidence.Name.Value); Assert.Equal(0, evidence.Name.Confidence);
    }

    internal static byte[] ImageBytes(int width = 200, int height = 300)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream(); image.SaveAsPng(stream); return stream.ToArray();
    }

    internal static HttpResponseMessage Response(string? evidence = null, string status = "completed", string contentType = "output_text") =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
        {
            status, model = "served-model-version", output = new[] { new { type = "message", role = "assistant", status = "completed",
                content = new[] { new { type = contentType, text = evidence ?? CardEvidenceTests.ValidJson } } } },
            usage = new { input_tokens = 300, output_tokens = 150 }
        })) };

    private static OpenAiCardEvidenceExtractor Extractor(Handler handler, OpenAiScannerOptions? options = null) =>
        new(new HttpClient(handler, false) { BaseAddress = new("https://api.openai.com/v1/"), Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(options ?? new() { Enabled = true, ApiKey = "server-test-key", RetryDelayMilliseconds = 0 }),
            NullLogger<OpenAiCardEvidenceExtractor>.Instance);

    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Interlocked.Increment(ref _calls); return send(request, ct); }
    }
}
