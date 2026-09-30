using System.Text.RegularExpressions;
using FuzzySharp;
using Microsoft.Extensions.Logging;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed partial class FuzzyCardSearchService(
    ICatalogSearch catalogSearch,
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

        var extracted = ExtractFields(ocrResult);
        if (string.IsNullOrWhiteSpace(extracted.Name))
        {
            logger.LogDebug("OCR did not extract a usable card name");
            return [];
        }

        // Search catalog by extracted name
        var searchResults = await catalogSearch.Search(
            extracted.Name, gameCode, 1, 20, cancellationToken);

        if (searchResults.Items.Count == 0)
            return [];

        var candidates = new List<(CatalogSearchResult Item, double Score)>();

        foreach (var item in searchResults.Items)
        {
            var score = CalculateScore(item, extracted);
            if (score > 0.3)
                candidates.Add((item, score));
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .Take(MaxCandidates)
            .Select(c => MapToCandidate(c.Item, c.Score))
            .ToArray();
    }

    private static ExtractedFields ExtractFields(OcrResult ocr)
    {
        var words = ocr.Regions
            .Where(r => !string.IsNullOrWhiteSpace(r.Text))
            .Select(r => r.Text.Trim())
            .ToList();

        var rawLines = ocr.RawText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Try to find collector number pattern (e.g., "12/102", "SV03-223", "045/198")
        string? collectorNumber = null;
        foreach (var word in words.Concat(rawLines))
        {
            if (CollectorNumberRegex().IsMatch(word))
            {
                collectorNumber = CollectorNumberRegex().Match(word).Value;
                break;
            }
        }

        // The card name is typically the longest meaningful text region or first prominent line
        var name = words
            .Where(w => w.Length >= 3 && !CollectorNumberRegex().IsMatch(w) && !SetKeywordRegex().IsMatch(w))
            .OrderByDescending(w => w.Length)
            .FirstOrDefault() ?? rawLines.FirstOrDefault(w => w.Length >= 3) ?? string.Empty;

        // Try to extract set name from remaining text
        var setName = words
            .Where(w => w != name && !CollectorNumberRegex().IsMatch(w))
            .FirstOrDefault(w => SetKeywordRegex().IsMatch(w) || w.Length > 5);

        return new ExtractedFields(name, collectorNumber, setName);
    }

    private static double CalculateScore(CatalogSearchResult item, ExtractedFields extracted)
    {
        var nameScore = Fuzz.Ratio(
            Normalize(item.CardName),
            Normalize(extracted.Name)) / 100.0;

        var numberScore = 0.0;
        if (!string.IsNullOrWhiteSpace(extracted.CollectorNumber))
        {
            numberScore = string.Equals(
                Normalize(item.CollectorNumber),
                Normalize(extracted.CollectorNumber),
                StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
        }

        var setScore = 0.0;
        if (!string.IsNullOrWhiteSpace(extracted.SetName))
        {
            setScore = Fuzz.PartialRatio(
                Normalize(item.SetName),
                Normalize(extracted.SetName)) / 100.0;
        }

        return (nameScore * NameWeight) + (numberScore * NumberWeight) + (setScore * SetWeight);
    }

    private static CardRecognitionCandidate MapToCandidate(CatalogSearchResult item, double score)
    {
        return new CardRecognitionCandidate(
            PrintingId: item.PrintingId.ToString(),
            Name: item.CardName,
            SetName: item.SetName,
            CollectorNumber: item.CollectorNumber,
            Rarity: item.Rarity,
            ArtworkUrl: item.ArtworkUrl,
            EstimatedMarketValueBrl: null,
            Currency: null,
            VariantCodes: ["normal"],
            ConfidenceScore: Math.Round(score, 3));
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.ToLowerInvariant().Trim();
    }

    [GeneratedRegex(@"\d+[/\-]\d+|[A-Z]{2,}\d{2,}-?\d+", RegexOptions.Compiled)]
    private static partial Regex CollectorNumberRegex();

    [GeneratedRegex(@"^(base|jungle|fossil|team|rocket|gym|neo|aquapolis|expedition|skyridge|ex|diamond|pearl|platinum|heartgold|soulsilver|black|white|xy|sun|moon|sword|shield|scarlet|violet|prismatic|evolutions|champions|destiny|dragon|legendary|mythical|ultra|shining|reverse|holo|promo|secret|rare|common|uncommon)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SetKeywordRegex();

    private sealed record ExtractedFields(string Name, string? CollectorNumber, string? SetName);
}