using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Application;

public interface ICardSearchProvider
{
    string GameCode { get; }
    Task<IReadOnlyList<CardRecognitionCandidate>> SearchByNameAsync(string query, CancellationToken cancellationToken);
}

public sealed class ScannerService(
    IEnumerable<ICardRecognitionProvider> recognitionProviders,
    IEnumerable<ICardSearchProvider> searchProviders,
    ICatalogSearch catalogSearch,
    IExternalIdResolver externalIdResolver)
{
    public async Task<CardScanResultDto> IdentifyAsync(CardScanRequest request, CancellationToken cancellationToken)
    {
        var gameCode = request.GameCode?.Trim().ToLowerInvariant();
        var candidates = new List<CardRecognitionCandidate>();

        foreach (var provider in recognitionProviders)
        {
            if (gameCode is not null && !string.Equals(provider.GameCode, gameCode, StringComparison.OrdinalIgnoreCase))
                continue;

            var results = await provider.IdentifyAsync(Convert.FromBase64String(request.ImageBase64), cancellationToken);
            candidates.AddRange(results);
        }

        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(gameCode))
        {
            var searchResults = await catalogSearch.Search("", gameCode, 1, 5, cancellationToken);
            foreach (var item in searchResults.Items)
            {
                candidates.Add(new CardRecognitionCandidate(
                    PrintingId: item.PrintingId.ToString(),
                    Name: item.CardName,
                    SetName: item.SetName,
                    CollectorNumber: item.CollectorNumber,
                    Rarity: item.Rarity,
                    ArtworkUrl: item.ArtworkUrl,
                    EstimatedMarketValueBrl: null,
                    Currency: null,
                    VariantCodes: ["normal"],
                    ConfidenceScore: 0.5));
            }
        }

        return await MapResult(candidates, cancellationToken);
    }

    public async Task<CardScanResultDto> SearchByNameAsync(string query, string? gameCode, CancellationToken cancellationToken)
    {
        var normalizedGame = gameCode?.Trim().ToLowerInvariant();
        var candidates = new List<CardRecognitionCandidate>();

        foreach (var provider in searchProviders)
        {
            if (normalizedGame is not null && !string.Equals(provider.GameCode, normalizedGame, StringComparison.OrdinalIgnoreCase))
                continue;

            var results = await provider.SearchByNameAsync(query, cancellationToken);
            candidates.AddRange(results);
        }

        if (candidates.Count == 0)
        {
            var searchResults = await catalogSearch.Search(query, normalizedGame, 1, 10, cancellationToken);
            foreach (var item in searchResults.Items)
            {
                candidates.Add(new CardRecognitionCandidate(
                    PrintingId: item.PrintingId.ToString(),
                    Name: item.CardName,
                    SetName: item.SetName,
                    CollectorNumber: item.CollectorNumber,
                    Rarity: item.Rarity,
                    ArtworkUrl: item.ArtworkUrl,
                    EstimatedMarketValueBrl: null,
                    Currency: null,
                    VariantCodes: ["normal"],
                    ConfidenceScore: 0.7));
            }
        }

        return await MapResult(candidates, cancellationToken);
    }

    private async Task<CardScanResultDto> MapResult(List<CardRecognitionCandidate> candidates, CancellationToken cancellationToken)
    {
        var ordered = candidates.OrderByDescending(c => c.ConfidenceScore).ToList();
        var externalIds = ordered
            .Where(c => !Guid.TryParse(c.PrintingId, out _))
            .Select(c => c.PrintingId)
            .Distinct()
            .ToList();

        var resolved = externalIds.Count > 0
            ? await externalIdResolver.ResolvePrintingIdsAsync(externalIds, cancellationToken)
            : new Dictionary<string, Guid>();

        var dtos = ordered
            .Select(c =>
            {
                var hasGuid = Guid.TryParse(c.PrintingId, out var guid);
                var printingId = hasGuid ? guid : (resolved.TryGetValue(c.PrintingId, out var resolvedId) ? resolvedId : Guid.Empty);
                var externalPrintingId = !hasGuid ? c.PrintingId : null;
                return new CardScanCandidateDto(
                    printingId,
                    c.Name,
                    c.SetName,
                    c.CollectorNumber,
                    c.Rarity,
                    c.ArtworkUrl,
                    c.EstimatedMarketValueBrl,
                    c.Currency,
                    c.VariantCodes,
                    c.ConfidenceScore,
                    externalPrintingId);
            })
            .ToArray();

        return new CardScanResultDto(dtos);
    }
}