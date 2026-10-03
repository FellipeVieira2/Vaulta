using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Infrastructure.MarketResearch;

public sealed class CompositeScannerWebMarketResearchProvider(JustTcgScannerMarketResearchProvider justTcg,
    OpenAiScannerWebMarketResearchProvider web) : IScannerWebMarketResearchProvider
{
    public async Task<ScannerMarketResearchResultDto?> ResearchAsync(CardVisualIdentificationDto identification, byte[]? image, CancellationToken ct)
    {
        var direct = await justTcg.ResearchAsync(identification, image, ct);
        return direct?.Estimate is not null ? direct : await web.ResearchAsync(identification, image, ct) ?? direct;
    }
}
