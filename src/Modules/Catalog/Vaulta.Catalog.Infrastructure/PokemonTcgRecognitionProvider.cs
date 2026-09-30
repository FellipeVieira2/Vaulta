using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Vaulta.Catalog.Application;

namespace Vaulta.Catalog.Infrastructure;

public sealed class PokemonTcgRecognitionProvider(
    HttpClient httpClient,
    ILogger<PokemonTcgRecognitionProvider> logger) : ICardRecognitionProvider, ICardSearchProvider
{
    public string GameCode => "pokemon";

    public async Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] imageData, CancellationToken cancellationToken)
    {
        // MVP: Image recognition requires external ML service. For now, we return empty
        // and the app should fallback to text search via ICatalogSearch.
        // Future: integrate with Google Vision / AWS Rekognition / custom model to extract
        // card name/set from the image, then query pokemontcg.io for full details + pricing.
        logger.LogWarning("Image-based card recognition not yet implemented. Use text search fallback.");
        await Task.CompletedTask;
        return [];
    }

    public async Task<IReadOnlyList<CardRecognitionCandidate>> SearchByNameAsync(string query, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"cards?q=name:\"{Uri.EscapeDataString(query)}\"&pageSize=5", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("PokemonTCG API returned {StatusCode} for query '{Query}'", (int)response.StatusCode, query);
            return [];
        }

        var result = await response.Content.ReadFromJsonAsync<PokemonTcgResponse>(cancellationToken: cancellationToken);
        if (result?.Data is null || result.Data.Length == 0)
            return [];

        return result.Data.Select(MapCandidate).ToArray();
    }

    private static CardRecognitionCandidate MapCandidate(PokemonTcgCard card)
    {
        var price = ExtractPrice(card);
        var variants = new List<string>();
        if (card.Holofoil == true) variants.Add("holo");
        if (card.ReverseHolofoil == true) variants.Add("reverse");
        if (variants.Count == 0) variants.Add("normal");

        return new CardRecognitionCandidate(
            PrintingId: card.Id,
            Name: card.Name ?? "Unknown",
            SetName: card.Set?.Name ?? "Unknown Set",
            CollectorNumber: card.Number ?? "?",
            Rarity: card.Rarity,
            ArtworkUrl: card.Images?.Large ?? card.Images?.Small,
            EstimatedMarketValueBrl: price,
            Currency: price.HasValue ? "USD" : null,
            VariantCodes: variants,
            ConfidenceScore: 0.95);
    }

    private static decimal? ExtractPrice(PokemonTcgCard card)
    {
        // Prefer TCGPlayer market price, fallback to Cardmarket averageSellPrice
        var tcgPrices = card.Tcgplayer?.Prices;
        if (tcgPrices is not null)
        {
            var market = tcgPrices.Normal?.Market ?? tcgPrices.Holofoil?.Market ?? tcgPrices.ReverseHolofoil?.Market;
            if (market.HasValue && market.Value > 0) return market.Value;
            var mid = tcgPrices.Normal?.Mid ?? tcgPrices.Holofoil?.Mid ?? tcgPrices.ReverseHolofoil?.Mid;
            if (mid.HasValue && mid.Value > 0) return mid.Value;
        }

        var cmPrices = card.Cardmarket?.Prices;
        if (cmPrices is not null)
        {
            if (cmPrices.AverageSellPrice.HasValue && cmPrices.AverageSellPrice.Value > 0)
                return cmPrices.AverageSellPrice.Value;
            if (cmPrices.TrendPrice.HasValue && cmPrices.TrendPrice.Value > 0)
                return cmPrices.TrendPrice.Value;
        }

        return null;
    }

    // --- DTOs for pokemontcg.io v2 response ---

    private sealed record PokemonTcgResponse(PokemonTcgCard[] Data);

    private sealed record PokemonTcgCard(
        string Id,
        string? Name,
        string? Number,
        string? Rarity,
        bool? Holofoil,
        bool? ReverseHolofoil,
        PokemonTcgSet? Set,
        PokemonTcgImages? Images,
        PokemonTcgTcgplayer? Tcgplayer,
        PokemonTcgCardmarket? Cardmarket);

    private sealed record PokemonTcgSet(string? Name);

    private sealed record PokemonTcgImages(string? Small, string? Large);

    private sealed record PokemonTcgTcgplayer(PokemonTcgPrices? Prices);
    private sealed record PokemonTcgCardmarket(PokemonTcgCmPrices? Prices);

    private sealed record PokemonTcgPrices(
        PokemonTcgPriceVariant? Normal,
        PokemonTcgPriceVariant? Holofoil,
        PokemonTcgPriceVariant? ReverseHolofoil);

    private sealed record PokemonTcgPriceVariant(decimal? Market, decimal? Mid, decimal? Low, decimal? High);

    private sealed record PokemonTcgCmPrices(
        decimal? AverageSellPrice,
        decimal? TrendPrice,
        decimal? LowPrice,
        decimal? Avg30);
}