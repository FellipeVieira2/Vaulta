using Microsoft.Extensions.Options;
using System.Text.Json;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.SharedKernel;

namespace Vaulta.Catalog.Infrastructure.MarketResearch;

public sealed class JustTcgScannerOptions
{
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
}
public sealed class JustTcgScannerMarketResearchProvider : IScannerWebMarketResearchProvider, IDisposable
{
    private readonly HttpClient _client;
    private readonly JustTcgScannerOptions _options;
    private readonly IBrlExchangeRateProvider _fx;
    private readonly IClock? _clock;
    private readonly SemaphoreSlim _capacity = new(2, 2);
    public JustTcgScannerMarketResearchProvider(HttpClient client, IOptions<JustTcgScannerOptions> options, IBrlExchangeRateProvider fx, IClock? clock = null)
    {
        _client = client; _options = options.Value; _fx = fx; _clock = clock;
        if (_options.TimeoutSeconds is < 1 or > 30) throw new ArgumentException("Invalid JustTCG timeout.", nameof(options));
    }
    public async Task<ScannerMarketResearchResultDto?> ResearchAsync(CardVisualIdentificationDto identification, byte[]? image, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || _options.ApiKey.Length > 512 || _options.ApiKey.Any(char.IsWhiteSpace)
            || identification.Certification?.IsGraded == true || string.IsNullOrWhiteSpace(identification.CollectorNumber)
            || string.IsNullOrWhiteSpace(identification.SetName) || string.IsNullOrWhiteSpace(identification.GameCode)) return null;
        var language = Language(identification.Language);
        var game = Game(identification.GameCode);
        var printing = Printing(identification.Finish);
        var condition = Condition(identification.Condition ?? "near-mint");
        if (language is null || game is null || printing is null || condition is null) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var entered = false;
        try
        {
            if (!await _capacity.WaitAsync(0, deadline.Token)) return null;
            entered = true;
            string Escape(string value) => Uri.EscapeDataString(value);
            var path = $"v1/cards?q={Escape(identification.Name)}&game={Escape(game)}&number={Escape(Number(identification.CollectorNumber))}&language={Escape(language)}&printing={Escape(printing)}&condition={Escape(condition)}&limit=20&include_price_history=false&include_statistics=";
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("x-api-key", _options.ApiKey);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await MarketResearchValidation.ReadBounded(response.Content, deadline.Token);
            if (bytes is null) return null;
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;
            var matches = data.EnumerateArray().Where(card => MarketResearchValidation.Same(identification.Name, MarketResearchValidation.String(card, "name"))
                && MarketResearchValidation.Same(game, Game(MarketResearchValidation.String(card, "game")))
                && MarketResearchValidation.Same(identification.SetName, MarketResearchValidation.String(card, "set_name"))
                && MarketResearchValidation.Same(Number(identification.CollectorNumber), Number(MarketResearchValidation.String(card, "number") ?? ""))).ToArray();
            if (matches.Length != 1 || !matches[0].TryGetProperty("variants", out var variants)) return null;
            var quotes = variants.EnumerateArray().Where(v => MarketResearchValidation.Same(language, MarketResearchValidation.String(v, "language") ?? "English")
                && MarketResearchValidation.Same(printing, MarketResearchValidation.String(v, "printing"))
                && MarketResearchValidation.Same(condition, Condition(MarketResearchValidation.String(v, "condition")))).ToArray();
            if (quotes.Length != 1) return null;
            var quote = quotes[0];
            if (!quote.TryGetProperty("price", out var price) || !price.TryGetDecimal(out var amount) || amount is <= 0 or > 1000000
                || !quote.TryGetProperty("lastUpdated", out var timestamp) || !timestamp.TryGetInt64(out var unix)) return null;
            var updated = DateTimeOffset.FromUnixTimeSeconds(unix);
            var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
            if (updated > now.AddMinutes(5) || updated < now.AddDays(-7)) return null;
            var cardId = MarketResearchValidation.String(matches[0], "id");
            if (string.IsNullOrWhiteSpace(cardId) || cardId.Length > 256) return null;
            // v1 is a North American USD reference, never label it as the Brazilian market.
            var sources = new[] { new ScannerPriceSourceDto($"https://api.justtcg.com/v1/cards?cardId={Escape(cardId)}&language={Escape(language)}&printing={Escape(printing)}&condition={Escape(condition)}",
                $"JustTCG North America · {language} · {printing} · {condition}", amount, "USD", $"market reference ({condition})", updated) };
            var brl = await MarketResearchValidation.ConvertAsync(sources, _fx, deadline.Token, now);
            if (brl is null) return null;
            var clean = MarketResearchValidation.WithoutSerial(identification);
            return new(clean, new(brl.Value, "JustTCG North America (USD reference)", now, clean.Confidence, clean, sources));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or ArgumentException or OverflowException) { return null; }
        finally { if (entered) _capacity.Release(); }
    }
    private static string? Language(string? value) => value?.ToLowerInvariant() switch
    { "en" => "English", "pt-br" or "pt" => "Portuguese", "ja" => "Japanese", "fr" => "French", "de" => "German", "es" => "Spanish", "it" => "Italian", "ko" => "Korean", "zh-hans" => "Chinese (S)", "zh-hant" => "Chinese (T)", "ru" => "Russian", _ => null };
    private static string? Game(string? value) => value?.ToLowerInvariant() switch
    { "pokemon" => "pokemon", "yugioh" or "yu-gi-oh" or "yu-gi-oh!" => "yu-gi-oh", "onepiece" or "one-piece-card-game" or "one piece card game" => "one-piece-card-game", _ => null };
    private static string Number(string value) => string.Join('/', value.Split('/').Select(piece =>
    {
        var clean = piece.Trim();
        return clean.Length > 0 && clean.All(char.IsAsciiDigit) ? (clean.TrimStart('0') is { Length: > 0 } significant ? significant : "0") : clean;
    }));
    private static string? Printing(string? value) => value?.ToLowerInvariant() switch
    { "normal" or "nonfoil" => "Normal", "reverse" or "reverse-holo" or "reverse-holofoil" => "Reverse Holofoil", "holo" or "holofoil" => "Holofoil", "foil" => "Foil", _ => null };
    private static string? Condition(string? value) => value?.ToLowerInvariant() switch
    { "near-mint" or "near mint" or "nm" => "Near Mint", "lightly-played" or "lightly played" or "lp" => "Lightly Played", "moderately-played" or "moderately played" or "mp" => "Moderately Played", "heavily-played" or "heavily played" or "hp" => "Heavily Played", "damaged" or "dmg" => "Damaged", _ => null };
    public void Dispose() => _capacity.Dispose();
}
