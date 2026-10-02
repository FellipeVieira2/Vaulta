using System.Text.RegularExpressions;
using FuzzySharp;
using Microsoft.Extensions.Logging;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed partial class FuzzyCardSearchService(
    ICardRecognitionCatalog catalogSearch,
    ILogger<FuzzyCardSearchService> logger)
{
    private const double NameWeight = 0.5;
    private const double NumberWeight = 0.35;
    private const double SetWeight = 0.15;
    private const int MaxCandidates = 5;

    public async Task<IReadOnlyList<CardRecognitionCandidate>> SearchAsync(
        OcrResult ocrResult, string gameCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ocrResult.RawText))
            return [];

        var extractedFields = ExtractFieldCandidates(ocrResult);
        if (extractedFields.Count == 0)
        {
            logger.LogDebug("OCR did not extract a usable card name");
            return [];
        }

        var candidates = new Dictionary<Guid, (RecognitionCatalogCard Item, double Score, bool NumberMatch)>();
        // OCR can read a decorative header before the name. Try a few header lines,
        // stopping when the name and collector number provide a strong match.
        foreach (var extracted in extractedFields)
        {
            var searchResults = await catalogSearch.FindCandidatesAsync(
                extracted.Name, extracted.CollectorNumber, gameCode, cancellationToken);
            foreach (var item in searchResults)
            {
                var score = CalculateScore(item, extracted);
                if (score >= 0.55 && (!candidates.TryGetValue(item.Card.PrintingId, out var previous) || previous.Score < score))
                    candidates[item.Card.PrintingId] = (item, score, extracted.CollectorNumber is not null &&
                        NormalizeNumber(item.Card.CollectorNumber) == NormalizeNumber(extracted.CollectorNumber));
            }
            if (candidates.Values.Any(x => x.NumberMatch && x.Score >= 0.93)) break;
        }

        return candidates.Values
            .OrderByDescending(c => c.Score)
            .Take(MaxCandidates)
            .Select(c => MapToCandidate(c.Item, c.Score, c.NumberMatch))
            .ToArray();
    }

    private static IReadOnlyList<ExtractedFields> ExtractFieldCandidates(OcrResult ocr)
    {
        var words = ocr.Regions
            .Where(r => !string.IsNullOrWhiteSpace(r.Text))
            .Select(r => r.Text.Trim())
            .ToList();

        var rawLines = ocr.RawText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Try to find collector number pattern (e.g., "12/102", "SV03-223", "045/198")
        string? collectorNumber = null;
        string? promoSet = null;
        string? printedLanguage = null;
        var promo = PromoFooterRegex().Match(string.Join(' ', rawLines.TakeLast(8)));
        if (promo.Success)
        {
            collectorNumber = promo.Groups["number"].Value;
            promoSet = promo.Groups["set"].Value.ToLowerInvariant();
            printedLanguage = promo.Groups["language"].Success ? promo.Groups["language"].Value.ToLowerInvariant() : null;
        }
        foreach (var word in rawLines.Reverse().Concat(words.AsEnumerable().Reverse()))
        {
            if (collectorNumber is not null) break;
            if (CollectorNumberRegex().IsMatch(word))
            {
                collectorNumber = CollectorNumberRegex().Match(word).Value;
                break;
            }
        }

        // Preserve complete names from the header, instead of selecting an attack/description word.
        var names = rawLines.Take(8)
            .Select(line => System.Text.RegularExpressions.Regex.Replace(line, @"\b(?:HP|PS|PV)\s*\d+|\d+\s*(?:HP|PS|PV)\b", "", RegexOptions.IgnoreCase).Trim())
            .Select(line => Regex.Replace(line, @"^(?:basic|básico|basico|trainer|treinador|stage\s*\d|estágio\s*\d|estagio\s*\d)\s*", "", RegexOptions.IgnoreCase).Trim(' ', '.', ',', '-', '·'))
            .Where(line => line.Length >= 3 && !CollectorNumberRegex().IsMatch(line)
                && !Regex.IsMatch(line, @"^(pok[eé]mon|energy|energia|item|supporter|apoiador|stadium|estádio|pokémon tool|ferramenta pokémon)$", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(line, @"^(basic|básico|basico|stage\s*\d|estágio\s*\d|estagio\s*\d|evolves from|evolui de|put .+ on the stage|coloque .+ sobre)\b", RegexOptions.IgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(4);

        // Card layouts usually do not print the expansion name; unrelated rules text is not set evidence.
        string? setName = null;

        // A single B immediately before digits can be a misread 8 in a numeric
        // collector number. Always try the printed prefix first (B8 may be valid).
        var numbers = new List<string?> { collectorNumber };
        if (collectorNumber is not null && Regex.IsMatch(collectorNumber, @"^B\s*\d+\s*/\s*\d+$", RegexOptions.IgnoreCase))
            numbers.Add(Regex.Replace(collectorNumber, @"^B\s*", "8", RegexOptions.IgnoreCase));

        return names.SelectMany(name => numbers.Select(number => new ExtractedFields(name, number, setName, promoSet, printedLanguage))).ToArray();
    }

    private static double CalculateScore(RecognitionCatalogCard match, ExtractedFields extracted)
    {
        var item = match.Card;
        if (extracted.PromoSet is not null && !string.Equals(match.ProviderSetId, extracted.PromoSet, StringComparison.OrdinalIgnoreCase)) return 0;
        if (extracted.PrintedLanguage is not null && !string.Equals(item.Language.Split('-')[0], extracted.PrintedLanguage, StringComparison.OrdinalIgnoreCase)) return 0;
        var nameScore = Fuzz.Ratio(
            Normalize(item.CardName),
            Normalize(extracted.Name)) / 100.0;

        var numberScore = 0.0;
        if (!string.IsNullOrWhiteSpace(extracted.CollectorNumber))
        {
            numberScore = string.Equals(
                NormalizeNumber(item.CollectorNumber),
                NormalizeNumber(extracted.CollectorNumber),
                StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
        }

        var setScore = 0.0;
        if (!string.IsNullOrWhiteSpace(extracted.SetName))
        {
            setScore = Fuzz.PartialRatio(
                Normalize(item.SetName),
                Normalize(extracted.SetName)) / 100.0;
        }

        if (nameScore < 0.55 || (extracted.CollectorNumber is not null && numberScore == 0)) return 0;
        var availableWeight = NameWeight + (extracted.CollectorNumber is not null ? NumberWeight : 0) + (extracted.SetName is not null ? SetWeight : 0);
        // A common Pokémon name alone identifies a character, not its printing.
        // Never present a name-only guess with the confidence of an exact card.
        var ceiling = extracted.CollectorNumber is null ? 0.7 : 0.95;
        return Math.Min(ceiling, ((nameScore * NameWeight) + (numberScore * NumberWeight) + (setScore * SetWeight)) / availableWeight);
    }

    private static CardRecognitionCandidate MapToCandidate(RecognitionCatalogCard match, double score, bool numberMatch)
    {
        var item = match.Card;
        return new CardRecognitionCandidate(
            PrintingId: item.PrintingId.ToString(),
            Name: item.CardName,
            SetName: item.SetName,
            CollectorNumber: item.CollectorNumber,
            Rarity: item.Rarity,
            ArtworkUrl: item.ArtworkUrl,
            EstimatedMarketValueBrl: null,
            Currency: null,
            VariantCodes: match.VariantCodes,
            ConfidenceScore: Math.Round(score, 3), HasCollectorNumberMatch: numberMatch);
    }

    private static string NormalizeNumber(string value) => Regex.Replace(value.Split('/')[0], @"\s+", "").ToUpperInvariant().TrimStart('0');

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.ToLowerInvariant().Trim();
    }

    [GeneratedRegex(@"\b(?:[A-Z]{1,4}\s*)?\d{1,4}\s*/\s*(?:[A-Z]{1,4}\s*)?\d{1,4}\b|[A-Z]{2,}\d{2,}-?\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CollectorNumberRegex();

    [GeneratedRegex(@"^(base|jungle|fossil|team|rocket|gym|neo|aquapolis|expedition|skyridge|ex|diamond|pearl|platinum|heartgold|soulsilver|black|white|xy|sun|moon|sword|shield|scarlet|violet|prismatic|evolutions|champions|destiny|dragon|legendary|mythical|ultra|shining|reverse|holo|promo|secret|rare|common|uncommon)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SetKeywordRegex();

    [GeneratedRegex(@"\b(?<set>MEP|SVP)\s*(?<language>PT|EN|ES|FR|DE|IT)?\s*(?<number>\d{1,4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PromoFooterRegex();

    private sealed record ExtractedFields(string Name, string? CollectorNumber, string? SetName, string? PromoSet, string? PrintedLanguage);
}
