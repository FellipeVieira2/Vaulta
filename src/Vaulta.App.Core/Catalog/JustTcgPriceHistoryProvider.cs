namespace Vaulta.App.Core.Catalog;

/// <summary>
/// Compatibility adapter while backend history and canonical provider mappings are unavailable.
/// Proprietary market credentials and provider calls belong to the backend.
/// </summary>
public sealed class JustTcgPriceHistoryProvider : IPriceHistoryProvider
{
    // Preserve existing callers without using demo data or attaching private keys to client requests.
    public JustTcgPriceHistoryProvider(HttpClient httpClient, SimulatedPriceHistoryProvider fallback, string? apiKey = null)
    {
    }

    public IReadOnlyList<PricePoint> GetPortfolioHistory(int days = 30) => [];

    public IReadOnlyList<PricePoint> GetCardPriceHistory(Guid printingId, int days = 30) => [];

    public IReadOnlyList<PricePoint> GetFilterPriceHistory(string filterKey, int days = 30) => [];

    /// <summary>
    /// A provider UUID alone does not establish canonical printing, variant, currency, or history provenance.
    /// Return unavailable until a trusted backend history contract supplies those facts.
    /// </summary>
    public Task<IReadOnlyList<PricePoint>?> GetVariantPriceHistoryAsync(
        string justTcgVariantUuid, int days = 30, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PricePoint>?>(Array.Empty<PricePoint>());
    }
}
