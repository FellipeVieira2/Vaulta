namespace Vaulta.App.Core.Catalog;

/// <summary>
/// Provides simulated price history data for charts until a real backend endpoint is available.
/// Generates deterministic, realistic-looking time series based on the input key so the same
/// card/filter always shows the same chart (avoiding visual jitter on re-renders).
/// </summary>
public interface IPriceHistoryProvider
{
    IReadOnlyList<PricePoint> GetPortfolioHistory(int days = 30);
    IReadOnlyList<PricePoint> GetCardPriceHistory(Guid printingId, int days = 30);
    IReadOnlyList<PricePoint> GetFilterPriceHistory(string filterKey, int days = 30);
}

public sealed record PricePoint(DateTimeOffset Date, double Value, string? Label = null);

public sealed class SimulatedPriceHistoryProvider : IPriceHistoryProvider
{
    public IReadOnlyList<PricePoint> GetPortfolioHistory(int days = 30)
    {
        // Portfolio starts higher and has gentler volatility than individual cards
        return GenerateSeries(days, baseValue: 12500, volatility: 0.018, seed: 42, labelPrefix: "Carteira");
    }

    public IReadOnlyList<PricePoint> GetCardPriceHistory(Guid printingId, int days = 30)
    {
        // Use the printingId hash as seed so each card gets a unique but stable curve
        var seed = printingId.GetHashCode();
        var baseValue = 50 + Math.Abs(seed % 450); // Cards range roughly R$50–R$500
        return GenerateSeries(days, baseValue, volatility: 0.035, seed, labelPrefix: null);
    }

    public IReadOnlyList<PricePoint> GetFilterPriceHistory(string filterKey, int days = 30)
    {
        var seed = StringComparer.OrdinalIgnoreCase.GetHashCode(filterKey ?? "all");
        var baseValue = 8000 + Math.Abs(seed % 7000);
        return GenerateSeries(days, baseValue, volatility: 0.022, seed, labelPrefix: filterKey);
    }

    private static IReadOnlyList<PricePoint> GenerateSeries(int days, double baseValue, double volatility, int seed, string? labelPrefix)
    {
        var random = new Random(seed);
        var points = new List<PricePoint>(days);
        var current = baseValue;
        var now = DateTimeOffset.UtcNow.Date;

        for (int i = days - 1; i >= 0; i--)
        {
            var date = now.AddDays(-i);
            // Mean-reverting random walk: drift toward baseValue + noise
            var drift = (baseValue - current) * 0.05;
            var noise = (random.NextDouble() - 0.5) * 2 * volatility * current;
            current = Math.Max(current + drift + noise, baseValue * 0.6);

            string? label = null;
            if (days <= 7)
                label = date.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
            else if (i % 5 == 0 || i == days - 1)
                label = date.ToString("dd/MM");

            points.Add(new PricePoint(date, Math.Round(current, 2), label));
        }

        return points;
    }
}