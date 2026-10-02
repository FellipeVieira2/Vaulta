using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Vaulta.Catalog.Application;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class OpenAiCardEvidenceExtractor : ICardEvidenceExtractor, IDisposable
{
    public const string PromptVersion = CardEvidenceOpenAiProtocol.PromptVersion;
    private const int MaxInputBytes = 15 * 1024 * 1024;
    private const int MaxResponseBytes = 65_536;
    private readonly HttpClient _client;
    private readonly OpenAiScannerOptions _options;
    private readonly ILogger<OpenAiCardEvidenceExtractor> _logger;
    private readonly SemaphoreSlim _capacity;

    public OpenAiCardEvidenceExtractor(HttpClient client, IOptions<OpenAiScannerOptions> options, ILogger<OpenAiCardEvidenceExtractor> logger)
    {
        _options = options.Value;
        if (!_options.IsValid()) throw new ArgumentException("Invalid OpenAI scanner limits.", nameof(options));
        _client = client; _logger = logger; _capacity = new(_options.MaxConcurrency, _options.MaxConcurrency);
    }

    public async Task<CardEvidence?> ExtractAsync(byte[] imageData, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = ScannerTelemetry.Activities.StartActivity("scanner.evidence.extract");
        activity?.SetTag("scanner.provider", "openai");
        activity?.SetTag("scanner.prompt.version", PromptVersion);
        activity?.SetTag("scanner.model.version", _options.Model);
        var tags = new[] { new KeyValuePair<string, object?>("provider", "openai"), new("model", _options.Model) };
        var timer = Stopwatch.StartNew(); var outcome = "unavailable"; var entered = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey)) { outcome = "not_configured"; return null; }
            if (imageData.Length is 0 or > MaxInputBytes) { outcome = "invalid_image"; return null; }
            // Fail fast under load, before decoding. No accumulating image/request queue.
            if (!await _capacity.WaitAsync(0, deadline.Token)) { outcome = "capacity"; return null; }
            entered = true;
            byte[]? image;
            using (var preprocessing = ScannerTelemetry.Activities.StartActivity("scanner.image.prepare"))
                image = await PrepareImage(imageData, deadline.Token);
            if (image is null) { outcome = "invalid_image"; return null; }
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var callActivity = ScannerTelemetry.Activities.StartActivity("scanner.openai.request");
                    callActivity?.SetTag("scanner.provider", "openai");
                    callActivity?.SetTag("scanner.model.version", _options.Model);
                    using var request = new HttpRequestMessage(HttpMethod.Post, "responses");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                    var payload = new Dictionary<string, object>
                    {
                        ["model"] = _options.Model, ["store"] = false, ["max_output_tokens"] = _options.MaxOutputTokens,
                        ["instructions"] = CardEvidenceOpenAiProtocol.Prompt,
                        ["input"] = new[] { new { role = "user", content = new[] { new { type = "input_image", image_url = "data:image/jpeg;base64," + Convert.ToBase64String(image), detail = _options.ImageDetail } } } },
                        ["text"] = new { format = new { type = "json_schema", name = "card_evidence_v1", strict = true, schema = CardEvidenceOpenAiProtocol.Schema } }
                    };
                    // Focused visual extraction: avoid the default medium reasoning consuming
                    // the small output budget. Keep overrides for older models compatible.
                    if (_options.Model == "gpt-6-luna" || _options.Model.StartsWith("gpt-6-luna-", StringComparison.Ordinal))
                        payload["reasoning"] = new { effort = "none" };
                    request.Content = JsonContent.Create(payload);
                    ScannerTelemetry.Calls.Add(1, tags);
                    using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                    callActivity?.SetTag("http.response.status_code", (int)response.StatusCode);
                    if (!response.IsSuccessStatusCode)
                    {
                        callActivity?.Dispose();
                        var transient = IsTransient(response.StatusCode);
                        if (transient && attempt < _options.RetryCount)
                        {
                            var retryAfter = response.Headers.RetryAfter;
                            var delay = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromMilliseconds(_options.RetryDelayMilliseconds * (1 << attempt)));
                            // Do not violate long provider backoffs or hold capacity indefinitely.
                            if (delay > TimeSpan.FromSeconds(1)) { outcome = "throttled"; return null; }
                            ScannerTelemetry.Retries.Add(1, tags);
                            await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, deadline.Token);
                            continue;
                        }
                        outcome = response.StatusCode == HttpStatusCode.TooManyRequests ? "throttled" : "unavailable";
                        return null;
                    }
                    var json = await ReadBounded(response.Content, deadline.Token);
                    callActivity?.Dispose();
                    if (json is null) { outcome = "invalid_response"; return null; }
                    using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
                    var root = document.RootElement;
                    RecordTokens(root, "input_tokens", "input", tags); RecordTokens(root, "output_tokens", "output", tags);
                    if (!TryOutput(root, out var text)) { outcome = "invalid_response"; return null; }
                    var evidence = CardEvidenceJsonParser.Parse(text!, PromptVersion, _options.Model);
                    outcome = evidence is null ? "invalid_response" : "success";
                    return evidence;
                }
                catch (HttpRequestException) when (attempt < _options.RetryCount)
                {
                    ScannerTelemetry.Retries.Add(1, tags);
                    await Task.Delay(_options.RetryDelayMilliseconds * (1 << attempt), deadline.Token);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { outcome = "timeout"; return null; }
        catch (OperationCanceledException) { outcome = "cancelled"; throw; }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException) { outcome = "invalid_image"; return null; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException) { outcome = "invalid_response"; return null; }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // Never pass provider exception/body/image to logging.
            _logger.LogWarning("Scanner evidence service unavailable; using configured fallback.");
            return null;
        }
        finally
        {
            if (entered) _capacity.Release();
            activity?.SetTag("scanner.outcome", outcome);
            var measurements = tags.Append(new KeyValuePair<string, object?>("outcome", outcome)).ToArray();
            ScannerTelemetry.Extractions.Add(1, measurements);
            ScannerTelemetry.Duration.Record(timer.Elapsed.TotalMilliseconds, measurements);
        }
    }

    private async Task<byte[]?> PrepareImage(byte[] bytes, CancellationToken ct)
    {
        using var source = new MemoryStream(bytes, false);
        var info = await Image.IdentifyAsync(source, ct);
        if (info.Metadata.DecodedImageFormat?.Name.ToLowerInvariant() is not ("jpeg" or "png" or "webp")
            || info.Width is < 100 or > 8000 || info.Height is < 100 or > 8000
            || (long)info.Width * info.Height > 40_000_000 || info.FrameMetadataCollection.Count > 1) return null;
        source.Position = 0;
        using var image = await Image.LoadAsync(new DecoderOptions { MaxFrames = 1 }, source, ct);
        image.Mutate(x => x.AutoOrient());
        if (image.Width > _options.MaxImageEdge || image.Height > _options.MaxImageEdge)
            image.Mutate(x => x.Resize(new ResizeOptions { Size = new(_options.MaxImageEdge, _options.MaxImageEdge), Mode = ResizeMode.Max }));
        image.Metadata.ExifProfile = null; image.Metadata.XmpProfile = null; image.Metadata.IptcProfile = null;
        using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = _options.JpegQuality, SkipMetadata = true }, ct);
        return output.Length <= 3_750_000 ? output.ToArray() : null;
    }

    private static async Task<byte[]?> ReadBounded(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxResponseBytes) return null;
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream(); var buffer = new byte[4096]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > MaxResponseBytes) return null;
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.ToArray();
    }

    private static bool TryOutput(JsonElement root, out string? text)
    {
        text = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status) || status.GetString() != "completed"
            || !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array || output.GetArrayLength() != 1) return false;
        var message = output[0];
        if (message.GetProperty("type").GetString() != "message" || message.GetProperty("role").GetString() != "assistant"
            || message.GetProperty("status").GetString() != "completed") return false;
        var content = message.GetProperty("content");
        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() != 1 || content[0].GetProperty("type").GetString() != "output_text") return false;
        text = content[0].GetProperty("text").GetString();
        return !string.IsNullOrWhiteSpace(text);
    }

    private static void RecordTokens(JsonElement root, string field, string direction, KeyValuePair<string, object?>[] tags)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) && count is >= 0 and <= 1_000_000)
            ScannerTelemetry.Tokens.Add(count, tags.Append(new KeyValuePair<string, object?>("direction", direction)).ToArray());
    }
    private static bool IsTransient(HttpStatusCode status) => status == HttpStatusCode.TooManyRequests || (int)status is >= 500 and <= 599;
    public void Dispose() { _capacity.Dispose(); _client.Dispose(); }
}
