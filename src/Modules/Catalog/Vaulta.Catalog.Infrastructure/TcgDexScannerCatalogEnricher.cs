using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Domain;

namespace Vaulta.Catalog.Infrastructure;

// A bounded catalog lookup after a local miss. No vision calls, prices or guessed IDs.
public sealed class TcgDexScannerCatalogEnricher(HttpClient client, ICatalogDiscoveryImporter importer,
    ILogger<TcgDexScannerCatalogEnricher> logger) : ICardEvidenceCatalogEnricher
{
    private const int MaxDetails = 5;
    private const int MaxResponseBytes = 131_072;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> EnrichAsync(CardEvidence evidence, string gameCode, CancellationToken cancellationToken)
    {
        if (gameCode != "pokemon" || !Reliable(evidence.GameCode) || evidence.GameCode.Value != gameCode
            || !Reliable(evidence.Name) || !Reliable(evidence.CollectorNumber) || !Reliable(evidence.Language)) return false;
        var number = evidence.CollectorNumber.Value!.Split('/');
        if (number.Length > 2) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var language = TcgDexProvider.NormalizeLanguage(evidence.Language.Value!.Split('-')[0]);
            var numberFilter = "eq:" + number[0].Trim() + "|" + NormalizeNumber(number[0]);
            var path = $"{language}/cards?name={Uri.EscapeDataString(evidence.Name.Value!)}&localId={Uri.EscapeDataString(numberFilter)}&pagination:page=1&pagination:itemsPerPage=51";
            var briefs = await Get<CardBrief[]>(path, deadline.Token);
            // Never silently truncate a larger candidate population into one identity.
            if (briefs is null || briefs.Length > 50) return false;
            var matches = briefs.Where(x => ValidIdentity(x.Id, x.Name)
                    && CatalogNormalizer.NormalizeName(x.Name) == CatalogNormalizer.NormalizeName(evidence.Name.Value!)
                    && SameNumber(Text(x.LocalId), number[0]))
                .DistinctBy(x => x.Id).ToArray();
            if (matches.Length is 0 or > MaxDetails) return false;
            var found = new List<ProviderSetDetails>();
            foreach (var brief in matches)
            {
                var card = await Get<CardDetails>($"{language}/cards/{Uri.EscapeDataString(brief.Id)}", deadline.Token);
                if (card is null || card.Id != brief.Id || !ValidIdentity(card.Id, card.Name)
                    || CatalogNormalizer.NormalizeName(card.Name) != CatalogNormalizer.NormalizeName(evidence.Name.Value!)
                    || !SameNumber(Text(card.LocalId), number[0])
                    || card.Set is null || !ValidIdentity(card.Set.Id, card.Set.Name) || card.Variants is null) continue;
                if (evidence.Hp is { Confidence: >= .8, Value: { } hp } && !SameNumber(Text(card.Hp), hp)) continue;
                var total = card.Set.CardCount?.Official;
                if (number.Length == 2 && (total is null || !SameNumber(total.Value.ToString(CultureInfo.InvariantCulture), number[1]))) continue;
                if (Reliable(evidence.SetName) && CatalogNormalizer.NormalizeName(evidence.SetName.Value!) != CatalogNormalizer.NormalizeName(card.Set.Name)) continue;
                // This imports catalog candidates, not confirmation of an unverified
                // physical set code. The matcher requires review when a code is visible.
                var variants = card.Variants.Where(x => x.Value)
                    .Select(x => new ProviderVariant(TcgDexProvider.VariantCode(x.Key), TcgDexProvider.VariantName(x.Key), x.Key))
                    .OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
                if (variants.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != variants.Length) continue;
                var collectorNumber = Text(card.LocalId)! + (total is > 0 ? "/" + total.Value.ToString(CultureInfo.InvariantCulture) : "");
                found.Add(new(new(card.Set.Id, card.Set.Name, null, null),
                    [new(card.Id, card.Name, collectorNumber, language, card.Rarity, TcgDexProvider.ArtworkUrl(card.Image), variants)]));
            }
            if (found.Count == 0) return false;
            var grouped = found.GroupBy(x => x.Set.ExternalId).Select(group => new ProviderSetDetails(group.First().Set,
                group.SelectMany(x => x.Printings).ToArray())).ToArray();
            return await importer.ImportAsync(grouped, deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or CatalogProviderException or OperationCanceledException)
        {
            logger.LogWarning("Scanner catalog discovery unavailable: {ErrorType}", ex.GetType().Name);
            return false;
        }
    }

    private async Task<T?> Get<T>(string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxResponseBytes) return default;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + read > MaxResponseBytes) return default;
            bytes.Write(buffer, 0, read);
        }
        return JsonSerializer.Deserialize<T>(bytes.ToArray(), JsonOptions);
    }
    private static bool Reliable(CardEvidenceField field) => !string.IsNullOrWhiteSpace(field.Value) && field.Confidence is >= .8 and <= 1;
    private static bool ValidIdentity(string? id, string? name) => id is { Length: > 0 and <= 100 } && name is { Length: > 0 and <= 160 }
        && Regex.IsMatch(id, "^[a-zA-Z0-9.-]+$", RegexOptions.CultureInvariant);
    private static string? Text(JsonElement value) => value.ValueKind switch
    { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null };
    private static bool SameNumber(string? left, string right) => left is not null && NormalizeNumber(left) == NormalizeNumber(right);
    private static string NormalizeNumber(string value) => Regex.Replace(Regex.Replace(value, @"\s+", "").ToUpperInvariant(), @"^(?<prefix>[A-Z]*)0+(?=\d)", "${prefix}");
    private sealed record CardBrief(string Id, string Name, JsonElement LocalId);
    private sealed record CardDetails(string Id, string Name, JsonElement LocalId, JsonElement Hp, string? Rarity, string? Image,
        Dictionary<string, bool>? Variants, CardSet? Set);
    private sealed record CardSet(string Id, string Name, CardCount? CardCount);
    private sealed record CardCount(int? Official);
}
