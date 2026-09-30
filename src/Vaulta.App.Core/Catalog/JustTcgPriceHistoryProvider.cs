using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vaulta.App.Core.Catalog;

/// <summary>
/// Consumes the JustTCG API v2 to provide real price history data for cards and portfolio.
/// Falls back to simulated data when the API is unavailable or no mapping exists.
/// Requires configuration: JustTcg:ApiKey in appsettings or user secrets.
/// </summary>
public sealed class JustTcgPriceHistoryProvider : IPriceHistoryProvider
{
    private readonly HttpClient _httpClient;
    private readonly SimulatedPriceHistoryProvider _fallback;
    private readonly string? _apiKey;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private sealed record CacheEntry(IReadOnlyList<PricePoint> Data, DateTimeOffset ExpiresAt);

    public JustTcgPriceHistoryProvider(HttpClient httpClient, SimulatedPriceHistoryProvider fallback, string? apiKey = null)
    {
        _httpClient = httpClient;
        _fallback = fallback;
        _apiKey = apiKey;

        if (!string.IsNullOrWhiteSpace(apiKey))
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
    }

    public IReadOnlyList<PricePoint> GetPortfolioHistory(int days = 30)
    {
        // Portfolio aggregation requires multiple card lookups; use fallback for now
        // TODO: Implement batch lookup of user's collection items and aggregate
        return _fallback.GetPortfolioHistory(days);
    }

    public IReadOnlyList<PricePoint> GetCardPriceHistory(Guid printingId, int days = 30)
    {
        // Without a direct UUID mapping, we cannot reliably query JustTCG by our internal printingId.
        // The CatalogExternalId table (Provider="justtcg") should be populated during catalog sync
        // to enable this lookup. Until then, fall back to simulated data.
        // TODO: Add ICatalogQueries.GetExternalId(printingId, "justtcg") and use it here
        return _fallback.GetCardPriceHistory(printingId, days);
    }

    public IReadOnlyList<PricePoint> GetFilterPriceHistory(string filterKey, int days = 30)
    {
        // Filter aggregation requires set-level or game-level queries; use fallback for now
        // TODO: Implement /v2/cards search with set/game filters and aggregate prices
        return _fallback.GetFilterPriceHistory(filterKey, days);
    }

    /// <summary>
    /// Direct lookup by JustTCG variant UUID — usable once CatalogExternalId mappings are populated.
    /// Returns null if the API call fails or no data is available.
    /// </summary>
    public async Task<IReadOnlyList<PricePoint>?> GetVariantPriceHistoryAsync(
        string justTcgVariantUuid, int days = 30, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return null;

        var period = days switch
        {
            <= 7 => "7d",
            <= 30 => "30d",
            <= 90 => "90d",
            _ => "1y"
        };
        var cacheKey = $"{justTcgVariantUuid}:{period}";

        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Data;

        try
        {
            var response = await _httpClient.GetAsync(
                $"v2/variants/{justTcgVariantUuid}?periods={period}", ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var variant = await response.Content.ReadFromJsonAsync<JustTcgVariantResponse>(
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }, ct);

            if (variant?.PriceHistory is null || variant.PriceHistory.Count == 0)
                return null;

            var points = variant.PriceHistory
                .Select(p => new PricePoint(
                    DateTimeOffset.FromUnixTimeSeconds(p.Timestamp),
                    (double)p.Price,
                    null))
                .OrderBy(p => p.Date)
                .ToList();

            _cache[cacheKey] = new CacheEntry(points, DateTimeOffset.UtcNow.Add(CacheDuration));
            return points;
        }
        catch
        {
            return null;
        }
    }

    // JustTCG API v2 response models
    private sealed record JustTcgVariantResponse(
        string? Uuid,
        decimal? Price,
        long? UpdatedAt,
        List<JustTcgPricePoint>? PriceHistory,
        Dictionary<string, JustTcgPeriodStats>? Periods);

    private sealed record JustTcgPricePoint(decimal Price, long Timestamp);

    private sealed record JustTcgPeriodStats(
        decimal? ChangePct,
        decimal? Avg,
        decimal? Min,
        decimal? Max,
        decimal? TrendSlope);
}