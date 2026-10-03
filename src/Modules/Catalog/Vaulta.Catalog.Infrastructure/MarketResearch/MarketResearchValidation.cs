using System.Text.Json;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Infrastructure.MarketResearch;

internal static class MarketResearchValidation
{
    public static CardVisualIdentificationDto WithoutSerial(CardVisualIdentificationDto card) => card with
    { Certification = card.Certification is null ? null : card.Certification with { Number = null } };

    public static bool Same(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool SafeUrl(string value) => value.Length <= 2048 && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && !uri.IsLoopback
        && !System.Net.IPAddress.TryParse(uri.Host, out _) && uri.Host.Contains('.');

    public static async Task<decimal?> ConvertAsync(IReadOnlyList<ScannerPriceSourceDto> sources, IBrlExchangeRateProvider fx,
        CancellationToken ct, DateTimeOffset? checkedAt = null)
    {
        decimal total = 0;
        foreach (var group in sources.GroupBy(x => x.Currency))
        {
            decimal rate = 1;
            if (group.Key != "BRL")
            {
                if (group.Key is not ("USD" or "EUR")) return null;
                var quote = await fx.GetAsync(group.Key, ct);
                var now = checkedAt ?? DateTimeOffset.UtcNow;
                if (quote is null || quote.Currency != group.Key || quote.Rate is <= 0 or > 1000
                    || quote.UpdatedAt > now.AddMinutes(5) || quote.UpdatedAt < now.AddDays(-10)) return null;
                rate = quote.Rate;
            }
            total += group.Sum(x => x.Amount * rate);
        }
        return sources.Count == 0 ? null : Math.Round(total / sources.Count, 2, MidpointRounding.AwayFromZero);
    }

    public static async Task<byte[]?> ReadBounded(HttpContent content, CancellationToken ct)
    {
        const int limit = 131072;
        if (content.Headers.ContentLength > limit) return null;
        using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var block = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(block, ct)) != 0)
        {
            if (buffer.Length + count > limit) return null;
            buffer.Write(block, 0, count);
        }
        return buffer.ToArray();
    }

    public static string? String(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
