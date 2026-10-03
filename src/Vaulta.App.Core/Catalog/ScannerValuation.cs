using Vaulta.Catalog.Contracts;

namespace Vaulta.App.Core.Catalog;

public static class ScannerValuation
{
    public static SessionMarketValue? SessionValue(ScannerCardDetailsDto details, Guid? variantId, CardVisualIdentificationDto? visual,
        ScannerMarketEstimateDto? estimate, bool confirmed = false)
    {
        var quote = ChooseQuote(details, variantId, visual);
        if (quote is not null) return new(decimal.Round(quote.MarketValueBrl, 2), quote.Source, quote.UpdatedAt,
            quote.OriginalValue, quote.OriginalCurrency, quote.ExchangeRate, quote.ExchangeRateAt);
        if (visual is null || !string.Equals(details.Printing.CardName, visual.Name, StringComparison.OrdinalIgnoreCase)
            || visual.CollectorNumber is { Length: > 0 } number && !string.Equals(number, details.Printing.CollectorNumber, StringComparison.OrdinalIgnoreCase)
            || visual.SetName is { Length: > 0 } set && !string.Equals(set, details.Printing.SetName, StringComparison.OrdinalIgnoreCase)
            || visual.Language is { Length: > 0 } language && !string.Equals(language.Split('-')[0], details.Printing.Language.Split('-')[0], StringComparison.OrdinalIgnoreCase)
            || visual.GameCode is { Length: > 0 } game && !string.Equals(game, details.Printing.GameCode, StringComparison.OrdinalIgnoreCase)) return null;
        if (variantId is { } id && details.Printing.Variants.FirstOrDefault(x => x.Id == id) is { } variant
            && !string.Equals(variant.Code, visual.Finish, StringComparison.OrdinalIgnoreCase)) return null;
        return ResearchValue(estimate, visual, confirmed);
    }
    public static SessionMarketValue? ResearchValue(ScannerMarketEstimateDto? estimate, CardVisualIdentificationDto? visual, bool confirmed = false)
    {
        if (estimate is null || visual is null || estimate.Identification is null || !IsValidVisual(visual) || !IsValidVisual(estimate.Identification)
            || !double.IsFinite(estimate.Confidence) || estimate.Confidence is < 0 or > 1
            || !confirmed && (estimate.Confidence < .8 || !CanAutoAcceptVisual(estimate.Identification) || !CanAutoAcceptVisual(visual))
            || estimate.AmountBrl <= 0 || decimal.Round(estimate.AmountBrl, 2) != estimate.AmountBrl
            || string.IsNullOrWhiteSpace(estimate.Source) || estimate.CheckedAt == default || estimate.Sources is not { Count: > 0 }
            || estimate.Sources.Any(x => x.Amount <= 0 || !Uri.TryCreate(x.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            || !CompatibleIdentity(visual, estimate.Identification)) return null;
        return new(estimate.AmountBrl, estimate.Source, estimate.CheckedAt, Sources: estimate.Sources,
            IsEstimate: estimate.IsEstimate, NextRefreshAt: estimate.NextRefreshAt);
    }

    private static bool CompatibleIdentity(CardVisualIdentificationDto a, CardVisualIdentificationDto b)
    {
        static bool Match(string? x, string? y) => string.Equals(x?.Trim(), y?.Trim(), StringComparison.OrdinalIgnoreCase);
        return Match(a.Name, b.Name) && Match(a.CollectorNumber, b.CollectorNumber) && Match(a.GameCode, b.GameCode)
            && Match(a.Language, b.Language) && Match(a.SetName, b.SetName) && Match(a.Finish, b.Finish)
            && (a.Certification?.IsGraded == true) == (b.Certification?.IsGraded == true)
            && (a.Certification?.IsGraded != true || Match(a.Certification.Company, b.Certification?.Company)
                && Match(a.Certification.Grade, b.Certification?.Grade));
    }
    public static bool IsValidVisual(CardVisualIdentificationDto visual) =>
        !string.IsNullOrWhiteSpace(visual.Name) && double.IsFinite(visual.Confidence) && visual.Confidence is >= 0 and <= 1;

    public static bool CanAutoAcceptVisual(CardVisualIdentificationDto? visual) =>
        visual is not null && IsValidVisual(visual) && visual.Confidence >= ScannerConfidencePolicy.AutoAcceptThreshold;

    public static CatalogVariantDto? SelectVariant(ScannerCardDetailsDto details, CardVisualIdentificationDto? visual)
    {
        var variants = details.Printing.Variants;
        if (!string.IsNullOrWhiteSpace(visual?.Finish))
            return variants.FirstOrDefault(x => string.Equals(x.Code, visual.Finish, StringComparison.OrdinalIgnoreCase));
        return visual is null && variants.Count == 1 ? variants[0] : null;
    }

    public static CardMarketQuoteDto? ChooseQuote(ScannerCardDetailsDto details, Guid? variantId, CardVisualIdentificationDto? visual = null) =>
        visual?.Certification is { IsGraded: true } ? null : details.MarketQuotes.Where(x => x.VariantId == variantId)
            .OrderBy(x => x.Source.Contains("TCGplayer", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();

    public static string? CertificationNotes(CardVisualIdentificationDto? visual) => visual?.Certification is { } label
        ? $"Certificação lida na foto, não verificada: {label.Company ?? "empresa ilegível"} · nota {label.Grade ?? "ilegível"} · certificado {label.Number ?? "ilegível"}."
        : null;
}
