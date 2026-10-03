using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;

namespace Vaulta.Catalog.Infrastructure;

public sealed class TcgDexOptions
{
    public string Language { get; set; } = "en";
    public string BaseAddress { get; set; } = "https://api.tcgdex.net/v2/";
    public int Timeout { get; set; } = 30;
    public int MaxConcurrency { get; set; } = 4;
    public int RetryCount { get; set; } = 3;
    public int MaxRetryDelaySeconds { get; set; } = 60;
}

public sealed class CatalogProviderException(string message, bool isTransient, string errorCategory = "provider", Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool IsTransient { get; } = isTransient;
    public string ErrorCategory { get; } = errorCategory;
}

internal sealed partial class TcgDexProvider(HttpClient httpClient, IOptions<TcgDexOptions> options) : ICatalogProvider
{
    private readonly TcgDexOptions _options = options.Value;
    public string Code => "tcgdex";
    public string Language => NormalizeLanguage(_options.Language);

    public async Task<IReadOnlyList<ProviderSet>> GetSets(CancellationToken cancellationToken)
    {
        var sets = await Get<TcgDexSetDto[]>("sets", cancellationToken);
        foreach (var set in sets)
        {
            if (set is null) throw ContractError("TCGdex set entry is null.");
            ValidateIdentity(set.Id, set.Name);
        }
        // TCGdex IDs are integration keys, not authoritative canonical set codes.
        return sets.Select(x => new ProviderSet(x.Id, x.Name, null, null)).ToArray();
    }

    public async Task<ProviderSetDetails> GetSetDetails(string setId, CancellationToken cancellationToken)
    {
        var set = await Get<TcgDexSetDetailsDto>($"sets/{Uri.EscapeDataString(setId)}", cancellationToken);
        ValidateIdentity(set.Id, set.Name);
        if (set.Id != setId || set.Cards is null)
            throw ContractError("TCGdex set identity or cards collection is invalid.");
        foreach (var brief in set.Cards)
        {
            if (brief is null) throw ContractError("TCGdex card brief is null.");
            ValidateIdentity(brief.Id, brief.Name);
        }
        if (set.Cards.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != set.Cards.Length)
            throw ContractError("TCGdex set contains duplicate card identities.");
        DateOnly? releaseDate = null;
        if (set.ReleaseDate is not null)
        {
            if (!DateOnly.TryParseExact(set.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw ContractError("TCGdex release date is invalid.");
            releaseDate = date;
        }
        var printings = new ProviderPrinting[set.Cards.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, set.Cards.Length), new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxConcurrency,
            CancellationToken = cancellationToken
        }, async (index, ct) =>
        {
            var brief = set.Cards[index];
            // Briefs omit rarity and treatments. Refresh details on every sync so metadata changes are observed.
            var card = await Get<TcgDexCardDetailsDto>($"cards/{Uri.EscapeDataString(brief.Id)}", ct);
            ValidateIdentity(card.Id, card.Name);
            if (card.Id != brief.Id || card.Set?.Id != setId)
                throw ContractError("TCGdex card does not match the requested card/set.");
            if (card.Variants is null) throw ContractError("TCGdex variants object is missing.");
            var number = CollectorNumber(card.LocalId);
            if (card.Set?.CardCount?.Official is > 0 and <= 9999)
                number += "/" + card.Set.CardCount.Official.Value.ToString(CultureInfo.InvariantCulture);
            var variants = card.VariantsDetailed is { Length: > 0 } detailed
                ? detailed.Select(DetailedVariant).DistinctBy(x => x.Code).OrderBy(x => x.Code, StringComparer.Ordinal).ToArray()
                : card.Variants.Where(x => x.Value)
                .Select(x => new ProviderVariant(VariantCode(x.Key), VariantName(x.Key), x.Key))
                .OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
            if (variants.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != variants.Length)
                throw ContractError("TCGdex treatments normalize to duplicate codes.");
            printings[index] = new ProviderPrinting(card.Id, card.Name, number, CultureInfo.GetCultureInfo(NormalizeLanguage(_options.Language)).Name,
                card.Rarity, ArtworkUrl(card.Image), variants,
                JsonSerializer.Serialize(new { hp = card.Hp, illustrator = card.Illustrator, category = card.Category, types = card.Types, stage = card.Stage, attacks = card.Attacks, abilities = card.Abilities }),
                card.Pricing is { ValueKind: JsonValueKind.Object } pricing ? pricing.GetRawText() : null);
        });
        return new(new ProviderSet(set.Id, set.Name, null, releaseDate, set.Serie is { } series ? new ProviderSeries(series.Id, series.Name) : null), printings);
    }

    private static ProviderVariant DetailedVariant(JsonElement detail)
    {
        if (detail.ValueKind != JsonValueKind.Object || !detail.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            throw ContractError("Detailed variant type is missing.");
        var parts = new List<string> { type.GetString()! };
        if (detail.TryGetProperty("subtype", out var subtype) && subtype.ValueKind == JsonValueKind.String) parts.Add(subtype.GetString()!);
        if (detail.TryGetProperty("stamp", out var stamps) && stamps.ValueKind == JsonValueKind.Array)
            parts.AddRange(stamps.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() == "1st-edition" ? "first-edition" : x.GetString()!).Order(StringComparer.Ordinal));
        if (detail.TryGetProperty("foil", out var foil) && foil.ValueKind == JsonValueKind.String) parts.Add(foil.GetString()!);
        if (detail.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.String && size.GetString() != "standard") parts.Add(size.GetString()!);
        var code = VariantCode(string.Join("-", parts));
        return new ProviderVariant(code, string.Join(" / ", parts), detail.GetRawText());
    }

    private async Task<T> Get<T>(string path, CancellationToken cancellationToken)
    {
        var language = NormalizeLanguage(_options.Language);
        for (var attempt = 0; ; attempt++)
        {
            TimeSpan? retryAfter = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.Timeout));
                using var response = await httpClient.GetAsync($"{Uri.EscapeDataString(language)}/{path}", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    retryAfter = response.Headers.RetryAfter?.Delta;
                    if (response.Headers.RetryAfter?.Date is { } retryDate) retryAfter = retryDate - DateTimeOffset.UtcNow;
                    var transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                    throw new CatalogProviderException($"TCGdex returned HTTP {(int)response.StatusCode}.", transient, $"http_{(int)response.StatusCode}");
                }
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken: timeout.Token)
                    ?? throw ContractError("TCGdex returned an empty response.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (JsonException e) { throw new CatalogProviderException("TCGdex response violates the JSON contract.", false, "invalid_json", e); }
            catch (Exception e) when (e is CatalogProviderException or HttpRequestException or OperationCanceledException)
            {
                var failure = e as CatalogProviderException ?? new CatalogProviderException("TCGdex request failed.", true,
                    e is OperationCanceledException ? "timeout" : "network", e);
                if (!failure.IsTransient || attempt >= _options.RetryCount) throw failure;
                // Do not retry before a long Retry-After, and do not hold a worker indefinitely either.
                if (retryAfter > TimeSpan.FromSeconds(_options.MaxRetryDelaySeconds)) throw failure;
                var delay = retryAfter ?? TimeSpan.FromMilliseconds(Math.Min(_options.MaxRetryDelaySeconds * 1000, 250 * Math.Pow(2, attempt)));
                await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, cancellationToken);
            }
        }
    }

    internal static string? ArtworkUrl(string? image)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        if (!Uri.TryCreate(image, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw ContractError("TCGdex artwork URL is invalid.");
        if (new[] { ".png", ".jpg", ".webp" }.Any(extension => uri.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))) return uri.AbsoluteUri;
        // https://tcgdex.dev/assets: card base path + /high.png (no download or mirroring).
        return uri.AbsoluteUri.TrimEnd('/') + "/high.png";
    }

    internal static string NormalizeLanguage(string language)
    {
        var normalized = language.Trim().Replace('_', '-').ToLowerInvariant();
        // Provider path codes are not arbitrary BCP47 locales. Portuguese is `pt`, not `pt-BR`.
        return normalized switch
        {
            "en" or "fr" or "es" or "it" or "pt" or "de" or "ja" or "ko" or "id" or "th" => normalized,
            "zh-tw" => "zh-tw", "zh-cn" => "zh-cn",
            _ => throw ContractError("Unsupported TCGdex language code.")
        };
    }

    private static string CollectorNumber(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String when !string.IsNullOrWhiteSpace(value.GetString()) => value.GetString()!,
        JsonValueKind.Number => value.GetRawText(),
        _ => throw ContractError("TCGdex collector number is missing or invalid.")
    };
    private static void ValidateIdentity(string? id, string? name)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw ContractError("TCGdex identity/name is missing.");
    }
    internal static string VariantCode(string raw)
    {
        var code = Domain.CatalogNormalizer.NormalizeName(CamelBoundary().Replace(raw, "$1 $2")).Replace(' ', '-');
        if (code.Length is 0 or > 80) throw ContractError("TCGdex treatment code is invalid.");
        return code;
    }
    internal static string VariantName(string raw) => raw switch
    {
        "normal" => "Normal", "reverse" => "Reverse", "holo" => "Holo", "firstEdition" => "First edition", "wPromo" => "W promo",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(VariantCode(raw).Replace('-', ' '))
    };
    private static CatalogProviderException ContractError(string message) => new(message, false, "invalid_contract");
    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex CamelBoundary();

    private sealed record TcgDexSetDto(string Id, string Name, TcgDexCardCountDto? CardCount);
    private sealed record TcgDexCardCountDto(int? Official);
    private sealed record TcgDexSetDetailsDto(string Id, string Name, string? ReleaseDate, TcgDexCardBriefDto[]? Cards, TcgDexSeriesDto? Serie);
    private sealed record TcgDexSeriesDto(string Id, string Name);
    private sealed record TcgDexCardBriefDto(string Id, string Name, JsonElement LocalId, string? Image);
    // Dictionary models the real boolean object and accepts future treatment keys without leaking them to the app.
    private sealed record TcgDexCardDetailsDto(string Id, string Name, JsonElement LocalId, string? Rarity, string? Image,
        Dictionary<string, bool>? Variants, TcgDexSetDto? Set,
        [property: JsonPropertyName("variants_detailed")] JsonElement[]? VariantsDetailed,
        int? Hp, string? Illustrator, string? Category, string[]? Types, string? Stage,
        JsonElement? Attacks, JsonElement? Abilities, JsonElement? Pricing);
}
