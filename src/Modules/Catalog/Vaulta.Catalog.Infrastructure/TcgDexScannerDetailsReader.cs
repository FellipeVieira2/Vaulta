using System.Globalization;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.SharedKernel;

namespace Vaulta.Catalog.Infrastructure;

public sealed class TcgDexScannerDetailsReader(HttpClient httpClient, CatalogDbContext db, ICatalogSearch catalog,
    IBrlExchangeRateProvider exchangeRates, IClock clock, ILogger<TcgDexScannerDetailsReader> logger) : IScannerCardDetailsReader
{
    public async Task<ScannerCardDetailsDto?> GetAsync(Guid printingId, CancellationToken cancellationToken)
    {
        var printing = await catalog.GetPrinting(printingId, cancellationToken);
        if (printing is null) return null;
        var day = MarketPriceDay.At(clock.UtcNow);
        var cached = await ReadSnapshot(printing, day, cancellationToken);
        if (cached is not null) return cached;

        // Database locks also coordinate separate API processes. Waiting readers
        // recheck the committed snapshot rather than querying the provider again.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"Vaulta:catalog:market:{printingId}")));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        day = MarketPriceDay.At(clock.UtcNow);
        cached = await ReadSnapshot(printing, day, cancellationToken);
        if (cached is not null) return cached;
        var externalId = await db.ExternalIds.AsNoTracking()
            .Where(x => x.Provider == "tcgdex" && x.EntityType == "printing" && x.EntityId == printingId)
            .Select(x => x.ExternalId).SingleOrDefaultAsync(cancellationToken);
        if (externalId is null) return new(printing, new Dictionary<string, string>(), [], "Informações adicionais indisponíveis para esta carta.");
        var language = TcgDexProvider.NormalizeLanguage(printing.Language);
        if (externalId.StartsWith(language + ":", StringComparison.Ordinal)) externalId = externalId[(language.Length + 1)..];
        try
        {
            using var response = await httpClient.GetAsync($"{Uri.EscapeDataString(language)}/cards/{Uri.EscapeDataString(externalId)}", cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var card = document.RootElement;
            if (card.ValueKind != JsonValueKind.Object || !card.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.String || id.GetString() != externalId) throw new JsonException("Provider returned a different card.");
            if (card.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.String)
                printing = printing with { ArtworkUrl = TcgDexProvider.ArtworkUrl(image.GetString()) ?? printing.ArtworkUrl };
            var identityRates = new Dictionary<string, BrlExchangeRate>
            {
                ["EUR"] = new("EUR", 1, clock.UtcNow), ["USD"] = new("USD", 1, clock.UtcNow)
            };
            var sourceQuotes = ReadQuotes(card, printing.Variants, identityRates);
            var currencies = sourceQuotes.Concat(ReadQuotes(card, printing.Variants,
                new Dictionary<string, BrlExchangeRate> { ["USD"] = identityRates["USD"] }))
                .Select(x => x.OriginalCurrency).Distinct();
            var rates = (await Task.WhenAll(currencies.Select(currency => exchangeRates.GetAsync(currency, cancellationToken))))
                .Where(x => x is not null).Select(x => x!).ToDictionary(x => x.Currency);
            var quotes = ReadQuotes(card, printing.Variants, rates);
            var result = new ScannerCardDetailsDto(printing, ReadInformation(card), quotes, quotes.Count == 0
                ? "Preço em reais indisponível. Não há preço ou cotação válida para esta variante."
                : "Referência internacional convertida para reais pela PTAX. As comparações usam médias do período e a mesma cotação de câmbio.");
            // Missing FX is a transient failure, not an unpriced card for the day.
            // Probe eligible source quotes with identity rates to distinguish them.
            if (sourceQuotes.Any(x => quotes.All(q => q.VariantId != x.VariantId))) return result;
            result = result with { FetchedAt = clock.UtcNow, NextRefreshAt = day.RefreshAfter };
            var snapshot = new DailyCardMarketSnapshot
            {
                PrintingId = printingId, MarketDay = day.Date, FetchedAt = result.FetchedAt.Value,
                RefreshAfter = day.RefreshAfter, Payload = JsonSerializer.Serialize(result)
            };
            db.DailyMarketSnapshots.Add(snapshot);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            // The reader is scoped but can be called repeatedly by collection valuation.
            db.Entry(snapshot).State = EntityState.Detached;
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Scanner details unavailable for {PrintingId}: {ErrorType}", printingId, ex.GetType().Name);
            return new(printing, new Dictionary<string, string>(), [], "Não foi possível atualizar as informações e os preços. Tente novamente.");
        }
    }

    private async Task<ScannerCardDetailsDto?> ReadSnapshot(CatalogPrintingDetails printing, MarketPriceDay day, CancellationToken ct)
    {
        var snapshot = await db.DailyMarketSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PrintingId == printing.PrintingId && x.MarketDay == day.Date, ct);
        if (snapshot is null || snapshot.RefreshAfter <= clock.UtcNow) return null;
        var result = JsonSerializer.Deserialize<ScannerCardDetailsDto>(snapshot.Payload)
            ?? throw new JsonException("Invalid persisted market snapshot.");
        return result with { Printing = printing with { ArtworkUrl = result.Printing.ArtworkUrl ?? printing.ArtworkUrl } };
    }

    internal static IReadOnlyDictionary<string, string> ReadInformation(JsonElement card)
    {
        var labels = new Dictionary<string, string>
        {
            ["category"] = "Categoria", ["illustrator"] = "Ilustrador", ["hp"] = "Pontos de vida", ["types"] = "Tipos",
            ["stage"] = "Estágio", ["suffix"] = "Versão", ["dexId"] = "Pokédex", ["evolveFrom"] = "Evolui de",
            ["description"] = "Descrição", ["abilities"] = "Habilidades", ["attacks"] = "Ataques",
            ["weaknesses"] = "Fraquezas", ["resistances"] = "Resistências", ["retreat"] = "Custo de recuo",
            ["effect"] = "Efeito", ["trainerType"] = "Tipo de treinador", ["energyType"] = "Tipo de energia",
            ["legal"] = "Formatos permitidos", ["regulationMark"] = "Marca de regulação", ["rarity"] = "Raridade",
            ["set"] = "Expansão", ["localId"] = "Número da carta", ["name"] = "Nome"
        };
        return card.EnumerateObject().Where(x => x.Name is not ("id" or "image" or "pricing" or "variants" or "variants_detailed" or "updated"))
            .Where(x => x.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            .ToDictionary(x => labels.GetValueOrDefault(x.Name, x.Name), x => FormatValue(x.Value));
    }

    private static string FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "", JsonValueKind.True => "Sim", JsonValueKind.False => "Não",
        JsonValueKind.Array => string.Join("; ", value.EnumerateArray().Select(FormatValue)),
        JsonValueKind.Object => string.Join(" · ", value.EnumerateObject()
            .Where(x => x.Name is not ("id" or "image" or "logo" or "symbol") && x.Value.ValueKind != JsonValueKind.Null)
            .Select(x => $"{NestedLabel(x.Name)}: {FormatValue(x.Value)}")), _ => value.ToString()
    };

    private static string NestedLabel(string name) => name switch
    {
        "name" => "Nome", "effect" => "Efeito", "damage" => "Dano", "cost" => "Custo",
        "type" => "Tipo", "value" => "Valor", "standard" => "Padrão", "expanded" => "Expandido",
        "total" => "Total", "official" => "Oficiais", "cardCount" => "Cartas", _ => name
    };

    internal static IReadOnlyList<CardMarketQuoteDto> ReadQuotes(JsonElement card, IReadOnlyList<CatalogVariantDto> variants, IReadOnlyDictionary<string, BrlExchangeRate> rates)
    {
        if (!card.TryGetProperty("pricing", out var pricing) || pricing.ValueKind != JsonValueKind.Object) return [];
        var quotes = new List<CardMarketQuoteDto>();
        foreach (var variant in variants)
        {
            if (variant.Code is "normal" or "holo" && rates.TryGetValue("EUR", out var eur)
                && pricing.TryGetProperty("cardmarket", out var market) && IsCurrency(market, "EUR") && UpdatedAt(market) is { } date)
            {
                var suffix = variant.Code == "holo" ? "-holo" : "";
                if (Number(market, "trend" + suffix) is { } value)
                {
                    var brl = ConvertBrl(value, eur.Rate);
                    var comparisons = new List<CardMarketComparisonDto>();
                    foreach (var days in new[] { 1, 7, 30 })
                    {
                        if (Number(market, $"avg{days}{suffix}") is not { } average) continue;
                        var averageBrl = ConvertBrl(average, eur.Rate);
                        if (averageBrl > 0) comparisons.Add(new(days, averageBrl, brl - averageBrl, Math.Round((brl - averageBrl) / averageBrl * 100, 2)));
                    }
                    quotes.Add(new(variant.Id, variant.Name, brl, value, "EUR", "Cardmarket", date, eur.Rate, eur.UpdatedAt, comparisons));
                    continue;
                }
            }
            if (!rates.TryGetValue("USD", out var usd) || !pricing.TryGetProperty("tcgplayer", out var tcg) || !IsCurrency(tcg, "USD") || UpdatedAt(tcg) is not { } updated) continue;
            var keys = variant.Code switch
            {
                "normal" => new[] { "normal" }, "holo" => ["holo", "holofoil"], "reverse" => ["reverse", "reverse-holofoil"],
                "first-edition" => ["1st-edition"], _ => []
            };
            foreach (var key in keys)
            {
                if (!tcg.TryGetProperty(key, out var data) || Number(data, "marketPrice") is not { } value) continue;
                quotes.Add(new(variant.Id, variant.Name, ConvertBrl(value, usd.Rate), value, "USD", "TCGplayer", updated, usd.Rate, usd.UpdatedAt, []));
                break;
            }
        }
        return quotes;
    }

    private static bool IsCurrency(JsonElement value, string currency) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("unit", out var unit) && unit.ValueKind == JsonValueKind.String && unit.GetString() == currency;
    private static decimal? Number(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetDecimal(out var amount) && amount > 0 ? amount : null;
    private static DateTimeOffset? UpdatedAt(JsonElement value) => value.TryGetProperty("updated", out var date) && date.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp) ? timestamp : null;
    private static decimal ConvertBrl(decimal amount, decimal rate) => Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero);
}
