using Vaulta.Catalog.Contracts;

namespace Vaulta.App.Core.Catalog;

public static class ScannerValuation
{
    public static CatalogVariantDto? SelectVariant(ScannerCardDetailsDto details, CardVisualIdentificationDto? visual)
    {
        var variants = details.Printing.Variants;
        if (!string.IsNullOrWhiteSpace(visual?.Finish))
            return variants.FirstOrDefault(x => string.Equals(x.Code, visual.Finish, StringComparison.OrdinalIgnoreCase));
        return variants.Count == 1 ? variants[0] : null;
    }

    public static CardMarketQuoteDto? ChooseQuote(ScannerCardDetailsDto details, Guid? variantId, CardVisualIdentificationDto? visual = null) =>
        visual?.Certification is { IsGraded: true } ? null : details.MarketQuotes.Where(x => x.VariantId == variantId)
            .OrderBy(x => x.Source.Contains("TCGplayer", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();

    public static string? CertificationNotes(CardVisualIdentificationDto? visual) => visual?.Certification is { } label
        ? $"Certificação lida na foto, não verificada: {label.Company ?? "empresa ilegível"} · nota {label.Grade ?? "ilegível"} · certificado {label.Number ?? "ilegível"}."
        : null;
}
