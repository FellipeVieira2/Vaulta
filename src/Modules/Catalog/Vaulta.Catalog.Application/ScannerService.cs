using Vaulta.Catalog.Contracts;
using Vaulta.SharedKernel;
using System.Text.RegularExpressions;

namespace Vaulta.Catalog.Application;

public interface ICardSearchProvider
{
    string GameCode { get; }
    Task<IReadOnlyList<CardRecognitionCandidate>> SearchByNameAsync(string query, CancellationToken cancellationToken);
}

public sealed class ScannerService(
    IEnumerable<ICardRecognitionProvider> recognitionProviders,
    ICatalogSearch catalogSearch,
    IExternalIdResolver externalIdResolver,
    IScannerMarketResearch? marketResearch = null,
    IScannerCardDetailsReader? detailsReader = null,
    IScannerIdentityResolver? identityResolver = null,
    IClock? clock = null)
{
    public async Task<CardScanResultDto> IdentifyAsync(CardScanRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        var callerToken = cancellationToken;
        cancellationToken = deadline.Token;
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
                    visual ??= new(name, Visible(evidence.CollectorNumber), Visible(evidence.Language), Visible(evidence.SetName), evidence.Name.Confidence,
                        Visible(evidence.GameCode), evidence.Hp is { Confidence: >= .8, Value: { } hp } && int.TryParse(hp, out var value) ? value : null,
                        ReadVariant(evidence), Visible(evidence.Condition),
                        ReadCertification(evidence), new(Visible(evidence.Rarity),
                            int.TryParse(Visible(evidence.Year), out var year) ? year : null, Visible(evidence.CardType), Visible(evidence.Stage)), Visible(evidence.Finish));
            }
            else candidates.AddRange(await provider.IdentifyAsync(image, cancellationToken));
        }

        var result = (await MapResult(candidates, cancellationToken)) with { VisualIdentification = visual, ServiceIssue = serviceIssue };
        if (visual is null) return result;
        try
        {
            var quote = await ExistingQuote(result, visual, cancellationToken);
            if (quote is not null) return result with { MarketEstimate = quote };
            if (marketResearch is null) return result;
            var researched = await marketResearch.ResearchAsync(visual, Convert.FromBase64String(request.ImageBase64), cancellationToken);
            if (researched is null) return result with { MarketIssue = "Preço pesquisado indisponível. O total permanece parcial." };
            var corrected = researched.Identification with { Certification = visual.Certification };
            var estimate = researched.Estimate is { } price ? price with { Identification = corrected } : null;
            result = result with { Candidates = SameIdentity(visual, corrected) ? result.Candidates : [],
                VisualIdentification = corrected, MarketEstimate = estimate, MarketIssue = researched.Issue };
            // A corrected reading goes through the existing matcher/importer, never a fabricated GUID or another vision call.
            if (identityResolver is not null && corrected.Confidence >= ScannerConfidencePolicy.AutoAcceptThreshold)
            {
                var correctedCandidates = await identityResolver.ResolveAsync(corrected, cancellationToken);
                result = result with { Candidates = (await MapResult(correctedCandidates.ToList(), cancellationToken)).Candidates };
            }
            result = result with { VisualIdentification = corrected, ServiceIssue = serviceIssue, MarketEstimate = estimate, MarketIssue = researched.Issue };
            if (estimate is null) result = result with { MarketEstimate = await ExistingQuote(result, corrected, cancellationToken) };
            return result;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Pricing failure must not discard a successful visual/canonical reading or expose provider response content.
            return result with { MarketIssue = "Não foi possível atualizar o preço. O total permanece parcial." };
        }
    }

    private async Task<ScannerMarketEstimateDto?> ExistingQuote(CardScanResultDto result, CardVisualIdentificationDto visual, CancellationToken ct)
    {
        if (detailsReader is null || visual.Certification?.IsGraded == true || visual.Confidence < .8
            || visual.CollectorNumber is null || visual.Language is null || visual.GameCode is null || visual.Finish is null) return null;
        var candidates = result.Candidates.Where(x => x.PrintingId != Guid.Empty && x.HasCollectorNumberMatch && x.ConfidenceScore >= .8).ToArray();
        if (candidates.Length != 1) return null;
        ScannerCardDetailsDto? details;
        try { details = await detailsReader.GetAsync(candidates[0].PrintingId, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
        if (details is null || details.NextRefreshAt is null || details.NextRefreshAt <= (clock?.UtcNow ?? DateTimeOffset.UtcNow)) return null;
        var card = details.Printing;
        if (!Same(card.GameCode, visual.GameCode) || !Same(card.Language, visual.Language) || !SameNumber(card.CollectorNumber, visual.CollectorNumber)
            || !Same(card.CardName, visual.Name) || visual.SetName is not null && !Same(card.SetName, visual.SetName)) return null;
        var variant = card.Variants.SingleOrDefault(x => Same(x.Code, visual.Finish));
        var quote = variant is null ? null : details.MarketQuotes.SingleOrDefault(x => x.VariantId == variant.Id && x.MarketValueBrl > 0);
        return quote is null ? null : new(quote.MarketValueBrl, quote.Source, details.FetchedAt ?? quote.UpdatedAt, visual.Confidence, visual, [], false, details.NextRefreshAt);
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool SameNumber(string a, string b)
    {
        static string Normalize(string number) => string.Join('/', number.Split('/').Select(part => Regex.Replace(
            Regex.Replace(part, @"\s+", "").ToUpperInvariant(), @"^(?<prefix>[A-Z]*)0+(?=\d)", "${prefix}")));
        return Normalize(a) == Normalize(b);
    }
    private static bool SameIdentity(CardVisualIdentificationDto a, CardVisualIdentificationDto b) => Same(a.Name, b.Name)
        && string.Equals(a.CollectorNumber, b.CollectorNumber, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Language, b.Language, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.SetName, b.SetName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.GameCode, b.GameCode, StringComparison.OrdinalIgnoreCase);

    private static string? Visible(CardEvidenceField? field) => field is { Confidence: >= ScannerConfidencePolicy.AutoAcceptThreshold and <= 1, Value: { } value } ? value : null;
    private static string? ReadVariant(CardEvidence evidence)
    {
        var variant = Visible(evidence.Variant) ?? Visible(evidence.Finish);
        // Texture and full-art layout are useful observations, but do not by
        // themselves resolve the priced normal/holo/reverse classification.
        return variant is "normal" or "holo" or "reverse" ? variant : null;
    }
    private static CardCertificationDto? ReadCertification(CardEvidence evidence) => Visible(evidence.IsGraded) == "true"
        || Visible(evidence.GradingCompany) is not null || Visible(evidence.Grade) is not null || Visible(evidence.CertificationNumber) is not null
        ? new(Visible(evidence.GradingCompany), Visible(evidence.Grade), Visible(evidence.CertificationNumber)) : null;

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
