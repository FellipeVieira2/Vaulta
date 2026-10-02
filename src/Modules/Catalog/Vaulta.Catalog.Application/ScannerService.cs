using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Application;

public interface ICardSearchProvider
{
    string GameCode { get; }
    Task<IReadOnlyList<CardRecognitionCandidate>> SearchByNameAsync(string query, CancellationToken cancellationToken);
}

public sealed class ScannerService(
    IEnumerable<ICardRecognitionProvider> recognitionProviders,
    ICatalogSearch catalogSearch,
    IExternalIdResolver externalIdResolver)
{
    public async Task<CardScanResultDto> IdentifyAsync(CardScanRequest request, CancellationToken cancellationToken)
    {
        var gameCode = request.GameCode?.Trim().ToLowerInvariant();
        if (gameCode is not null && !recognitionProviders.Any(p => p.GameCode.Equals(gameCode, StringComparison.OrdinalIgnoreCase)))
            throw new Vaulta.SharedKernel.DomainException("O scanner ainda não suporta este jogo.");
        var candidates = new List<CardRecognitionCandidate>();
        CardVisualIdentificationDto? visual = null;
        string? serviceIssue = null;

        foreach (var provider in recognitionProviders)
        {
            if (gameCode is not null && !string.Equals(provider.GameCode, gameCode, StringComparison.OrdinalIgnoreCase))
                continue;

            var image = Convert.FromBase64String(request.ImageBase64);
            if (provider is IVisualCardRecognitionProvider vision)
            {
                var reading = await vision.IdentifyWithEvidenceAsync(image, cancellationToken);
                candidates.AddRange(reading.Candidates);
                serviceIssue ??= reading.ServiceIssue;
                if (reading.Evidence is { Name.Value: { } name, Name.Confidence: >= .65 } evidence)
                    visual ??= new(name, evidence.CollectorNumber.Value, evidence.Language.Value, evidence.SetName.Value, evidence.Name.Confidence,
                        evidence.GameCode.Value, evidence.Hp is { Confidence: >= .8, Value: { } hp } && int.TryParse(hp, out var value) ? value : null);
            }
            else candidates.AddRange(await provider.IdentifyAsync(image, cancellationToken));
        }

        return (await MapResult(candidates, cancellationToken)) with { VisualIdentification = visual, ServiceIssue = serviceIssue };
    }

    public async Task<CardScanResultDto> SearchByNameAsync(string query, string? gameCode, CancellationToken cancellationToken)
    {
        var normalizedGame = gameCode?.Trim().ToLowerInvariant();
        var candidates = new List<CardRecognitionCandidate>();

        var searchResults = await catalogSearch.Search(query, normalizedGame, 1, 10, cancellationToken);
        foreach (var item in searchResults.Items)
        {
            var printing = await catalogSearch.GetPrinting(item.PrintingId, cancellationToken);
            candidates.Add(new CardRecognitionCandidate(
                PrintingId: item.PrintingId.ToString(),
                Name: item.CardName,
                SetName: item.SetName,
                CollectorNumber: item.CollectorNumber,
                Rarity: item.Rarity,
                ArtworkUrl: item.ArtworkUrl,
                EstimatedMarketValueBrl: null,
                Currency: null,
                VariantCodes: printing?.Variants.Select(x => x.Code).ToArray() ?? [],
                ConfidenceScore: 0.7));
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
                    externalPrintingId,
                    c.HasCollectorNumberMatch);
            })
            .ToArray();

        return new CardScanResultDto(dtos);
    }
}
