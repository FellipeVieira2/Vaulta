using System.Net.Http.Headers;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure.Recognition;
using Vaulta.SharedKernel;

namespace Vaulta.Catalog.Infrastructure.MarketResearch;

public sealed class OpenAiScannerWebMarketResearchProvider : IScannerWebMarketResearchProvider, IDisposable
{
    private readonly HttpClient _client;
    private readonly OpenAiScannerOptions _options;
    private readonly IBrlExchangeRateProvider _fx;
    private readonly ScannerMarketResearchOptions _research;
    private readonly SemaphoreSlim _capacity;
    private readonly IClock? _clock;
    private DateTimeOffset Now => _clock?.UtcNow ?? DateTimeOffset.UtcNow;
    public OpenAiScannerWebMarketResearchProvider(HttpClient client, IOptions<OpenAiScannerOptions> options, IBrlExchangeRateProvider fx, IClock? clock = null,
        IOptions<ScannerMarketResearchOptions>? researchOptions = null)
    {
        _client = client; _options = options.Value; _fx = fx; _clock = clock;
        _research = researchOptions?.Value ?? new();
        if (!_options.IsValid()) throw new ArgumentException("Invalid scanner research limits.", nameof(options));
        if (!_research.IsValid()) throw new ArgumentException("Invalid market research limits.", nameof(researchOptions));
        _capacity = new(_research.MaxConcurrency, _research.MaxConcurrency);
    }
    public async Task<ScannerMarketResearchResultDto?> ResearchAsync(CardVisualIdentificationDto identification, byte[]? image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_research.Enabled || !_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(identification.Name)) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_research.TimeoutSeconds));
        var entered = false;
        var timer = Stopwatch.StartNew();
        var outcome = "unavailable";
        using var activity = ScannerTelemetry.Activities.StartActivity("scanner.market.web");
        activity?.SetTag("scanner.model", _options.Model);
        try
        {
            if (!await _capacity.WaitAsync(0, deadline.Token)) return null;
            entered = true;
            var clean = MarketResearchValidation.WithoutSerial(identification);
            var content = new List<object> { new { type = "input_text", text = JsonSerializer.Serialize(clean) } };
            if (image is not null)
            {
                if (image.Length is 0 or > 15 * 1024 * 1024) return null;
                using var source = new MemoryStream(image, false);
                var info = await Image.IdentifyAsync(source, deadline.Token);
                if (info.Metadata.DecodedImageFormat?.Name.ToLowerInvariant() is not ("jpeg" or "png" or "webp")
                    || info.Width is < 100 or > 8000 || info.Height is < 100 or > 8000
                    || (long)info.Width * info.Height > 40_000_000 || info.FrameMetadataCollection.Count > 1) return null;
                source.Position = 0;
                using var photo = await Image.LoadAsync(new DecoderOptions { MaxFrames = 1 }, source, deadline.Token);
                photo.Mutate(x => x.AutoOrient().Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new(_options.MaxImageEdge, _options.MaxImageEdge) }));
                photo.Metadata.ExifProfile = null; photo.Metadata.XmpProfile = null; photo.Metadata.IptcProfile = null;
                using var encoded = new MemoryStream();
                await photo.SaveAsJpegAsync(encoded, new JpegEncoder { Quality = _options.JpegQuality, SkipMetadata = true }, deadline.Token);
                if (encoded.Length > 4 * 1024 * 1024) return null;
                content.Add(new { type = "input_image", image_url = "data:image/jpeg;base64," + Convert.ToBase64String(encoded.ToArray()), detail = _options.ImageDetail });
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, "responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            var payload = new Dictionary<string, object>
            {
                ["model"] = _options.Model, ["store"] = false, ["max_output_tokens"] = _research.MaxOutputTokens,
                ["max_tool_calls"] = _research.MaxToolCalls, ["tools"] = new[] { new { type = "web_search", search_context_size = "low" } },
                ["include"] = new[] { "web_search_call.action.sources" }, ["tool_choice"] = "required",
                ["instructions"] = ScannerMarketWebProtocol.Prompt,
                ["input"] = new[] { new { role = "user", content } },
                ["text"] = new { format = new { type = "json_schema", name = "scanner_market_v1", strict = true, schema = ScannerMarketWebProtocol.Schema } }
            };
            if (_options.Model == "gpt-6-luna" || _options.Model.StartsWith("gpt-6-luna-", StringComparison.Ordinal)) payload["reasoning"] = new { effort = "none" };
            request.Content = JsonContent.Create(payload);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await MarketResearchValidation.ReadBounded(response.Content, deadline.Token);
            if (bytes is null) return null;
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
            if (doc.RootElement.TryGetProperty("usage", out var usage))
                foreach (var tokenType in new[] { "input_tokens", "output_tokens" })
                    if (usage.TryGetProperty(tokenType, out var tokenCount) && tokenCount.TryGetInt64(out var count) && count >= 0)
                        ScannerMarketTelemetry.Tokens.Add(count, new KeyValuePair<string, object?>("model", _options.Model), new("type", tokenType));
            var result = await ParseAsync(doc.RootElement, clean, image is not null, deadline.Token);
            outcome = result?.Estimate is not null ? "quoted" : result is not null ? "identity_only" : "unverified";
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException
            or FormatException or KeyNotFoundException or ArgumentException or UnknownImageFormatException or InvalidImageContentException or OverflowException) { return null; }
        finally
        {
            if (entered) _capacity.Release();
            activity?.SetTag("scanner.outcome", outcome);
            ScannerMarketTelemetry.Duration.Record(timer.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("model", _options.Model), new("outcome", outcome));
        }
    }

    private async Task<ScannerMarketResearchResultDto?> ParseAsync(JsonElement root, CardVisualIdentificationDto input, bool hasPhoto, CancellationToken ct)
    {
        if (MarketResearchValidation.String(root, "status") != "completed" || !root.TryGetProperty("output", out var output)) return null;
        var urls = new HashSet<string>(StringComparer.Ordinal);
        string? text = null;
        foreach (var item in output.EnumerateArray())
        {
            if (MarketResearchValidation.String(item, "type") == "web_search_call" && MarketResearchValidation.String(item, "status") == "completed"
                && item.TryGetProperty("action", out var action))
            {
                if (action.TryGetProperty("sources", out var sources))
                    foreach (var source in sources.EnumerateArray())
                        if (MarketResearchValidation.String(source, "url") is { } url && MarketResearchValidation.SafeUrl(url)) urls.Add(url);
                if (MarketResearchValidation.String(action, "type") == "open_page" && MarketResearchValidation.String(action, "url") is { } opened
                    && MarketResearchValidation.SafeUrl(opened)) urls.Add(opened);
            }
            if (MarketResearchValidation.String(item, "type") == "message" && item.TryGetProperty("content", out var content))
                foreach (var part in content.EnumerateArray())
                    if (MarketResearchValidation.String(part, "type") == "output_text") text = MarketResearchValidation.String(part, "text");
        }
        if (urls.Count == 0 || text is null) return null;
        using var data = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
        var p = data.RootElement;
        string? S(string name) => MarketResearchValidation.String(p, name);
        if (p.GetProperty("conflict").GetBoolean()) return null;
        var confidence = p.GetProperty("confidence").GetDouble();
        if (!double.IsFinite(confidence) || confidence is < 0 or > 1) return null;
        if (!MarketResearchValidation.Same(input.Name, S("name")) || !MarketResearchValidation.Same(input.GameCode, S("game"))) return null;
        // Known language, variant and grading evidence cannot be replaced by a convenient market.
        if (input.Language is not null && !MarketResearchValidation.Same(input.Language, S("language"))) return null;
        if (input.Finish is not null && !MarketResearchValidation.Same(input.Finish, S("finish"))) return null;
        if (input.SetName is not null && !MarketResearchValidation.Same(input.SetName, S("set"))) return null;
        if (input.Condition is not null && !MarketResearchValidation.Same(input.Condition, S("condition"))) return null;
        if (input.Certification?.IsGraded == true)
        {
            if (!MarketResearchValidation.Same(input.Certification.Company, S("company")) || !MarketResearchValidation.Same(input.Certification.Grade, S("grade"))) return null;
        }
        else if (S("company") is not null || S("grade") is not null) return null;
        if (string.IsNullOrWhiteSpace(S("number")) || string.IsNullOrWhiteSpace(S("set"))) return null;
        if (!MarketResearchValidation.Same(input.CollectorNumber, S("number")) && (!hasPhoto || !p.GetProperty("photoSupported").GetBoolean())) return null;
        var references = p.GetProperty("identitySourceUrls").EnumerateArray().Select(x => x.GetString()).ToArray();
        if (references.Length is 0 or > 8 || references.Any(x => x is null || !urls.Contains(x))) return null;
        var photoSupported = hasPhoto && p.GetProperty("photoSupported").GetBoolean();
        var resolved = input with { CollectorNumber = S("number"), SetName = S("set"), Confidence = confidence,
            Language = input.Language ?? (photoSupported ? S("language") : null), Finish = input.Finish ?? (photoSupported ? S("finish") : null) };
        var found = new List<ScannerPriceSourceDto>();
        foreach (var source in p.GetProperty("sources").EnumerateArray())
        {
            foreach (var key in new[] { "name", "game", "number", "language", "set", "finish", "company", "grade", "condition" })
            {
                var expected = S(key);
                var actual = MarketResearchValidation.String(source, key);
                if (expected is null ? actual is not null : !MarketResearchValidation.Same(expected, actual)) return null;
            }
            var url = MarketResearchValidation.String(source, "url");
            var title = MarketResearchValidation.String(source, "title");
            var currency = MarketResearchValidation.String(source, "currency");
            var basis = MarketResearchValidation.String(source, "basis");
            if (currency is not ("BRL" or "USD" or "EUR")) return new(resolved, null, "price_unavailable");
            DateTimeOffset? date = null;
            if (source.GetProperty("date").ValueKind != JsonValueKind.Null)
            {
                if (!DateTimeOffset.TryParse(MarketResearchValidation.String(source, "date"), out var parsed)
                    || parsed > Now.AddMinutes(5) || parsed < Now.AddDays(-90)) return null;
                date = parsed;
            }
            if (url is null || !urls.Contains(url) || string.IsNullOrWhiteSpace(title) || title.Length > 512
                || basis is not ("asking" or "sold" or "market")
                || !source.GetProperty("amount").TryGetDecimal(out var amount) || amount is <= 0 or > 1000000) return null;
            if (found.Any(x => x.Url == url)) return null;
            var reference = S("condition");
            found.Add(new(url, title, amount, currency, reference is null ? basis : $"{basis} ({reference} reference)", date));
        }
        if (found.Count == 0) return new(resolved, null, "price_unavailable");
        if (found.Count > 8) return null;
        if (string.IsNullOrWhiteSpace(resolved.Language) || string.IsNullOrWhiteSpace(resolved.Finish)) return new(resolved, null, "variant_unresolved");
        decimal? value;
        try { value = await MarketResearchValidation.ConvertAsync(found, _fx, ct, Now); }
        catch (Exception ex) when (ex is HttpRequestException or IOException
            || ex is OperationCanceledException && !ct.IsCancellationRequested)
        { return new(resolved, null, "exchange_rate_unavailable"); }
        if (value is null) return new(resolved, null, "exchange_rate_unavailable");
        return new(resolved, new(value.Value, "OpenAI web research", Now, confidence, resolved, found));
    }
    public void Dispose() => _capacity.Dispose();
}

