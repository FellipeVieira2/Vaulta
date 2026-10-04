namespace Vaulta.App.Core.Catalog;

public enum ScannerRevealKind { Price, NoPrice, VariantPending, GradedUnavailable }

/// <summary>Presentation only: never adds a card, changes a quote or persists a total.</summary>
public sealed record ScannerRevealPresentation(ScannerRevealKind Kind, decimal? Value, decimal PreviousTotal, decimal FinalTotal)
{
    public static ScannerRevealPresentation Create(ScannerSessionCard card, decimal previousTotal, decimal finalTotal)
    {
        var kind = card.VisualIdentification?.Certification is not null ? ScannerRevealKind.GradedUnavailable
            : card.VariantId is null ? ScannerRevealKind.VariantPending
            : card.MarketValue is null ? ScannerRevealKind.NoPrice : ScannerRevealKind.Price;
        return new(kind, kind == ScannerRevealKind.Price ? card.MarketValue!.AmountBrl : null, previousTotal, finalTotal);
    }
    public decimal? ValueAt(double progress) => Value is {} value ? decimal.Round(value * Fraction(progress), 2) : null;
    public decimal TotalAt(double progress) => decimal.Round(PreviousTotal + (FinalTotal - PreviousTotal) * Fraction(progress), 2);
    private static decimal Fraction(double progress) => (decimal)(double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0);
}
