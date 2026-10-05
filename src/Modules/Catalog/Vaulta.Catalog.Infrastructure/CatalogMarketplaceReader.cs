using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Domain;

namespace Vaulta.Catalog.Infrastructure;

public sealed class CatalogMarketplaceReader(CatalogDbContext db, ILogger<CatalogMarketplaceReader> logger) : ICatalogMarketplaceReader
{
    public async Task<IReadOnlyList<Guid>> FindPrintingIds(string? query, string? gameCode, Guid? setId, string? language, CancellationToken ct)
    {
        var items = db.Printings.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var name = CatalogNormalizer.NormalizeName(query);
            var number = CatalogNormalizer.NormalizeCollectorNumber(query);
            if (name.Length == 0) return [];
            items = items.Where(x => x.Card.NormalizedName.Contains(name) || x.Set.NormalizedName.Contains(name) || x.NormalizedCollectorNumber.Contains(number));
        }
        if (!string.IsNullOrWhiteSpace(gameCode)) { var game = gameCode.Trim().ToLowerInvariant(); items = items.Where(x => x.Card.Game.Code == game); }
        if (setId.HasValue) items = items.Where(x => x.SetId == setId.Value);
        if (!string.IsNullOrWhiteSpace(language)) { var code = language.Trim().ToLowerInvariant(); items = items.Where(x => x.Language.ToLower() == code); }
        return await items.Select(x => x.Id).ToArrayAsync(ct);
    }
    public async Task<IReadOnlyList<Guid>> FindVariantIds(string? code, IReadOnlyCollection<Guid> printingIds, CancellationToken ct) =>
        await db.Variants.AsNoTracking().Where(x => x.IsActive && printingIds.Contains(x.PrintingId) && (code == null || x.Code == code))
            .Select(x => x.Id).ToArrayAsync(ct);

    public async Task<CatalogMarketplaceFilterOptions> GetFilterOptions(string? gameCode, string? setQuery, int page, int pageSize, CancellationToken ct)
    {
        if (setQuery?.Length > 160) throw new Vaulta.SharedKernel.DomainException("Set search is too long.");
        var paging = Vaulta.SharedKernel.Pagination.Normalize(page,pageSize);
        var game = string.IsNullOrWhiteSpace(gameCode) ? null : gameCode.Trim().ToLowerInvariant();
        var sets = db.Sets.AsNoTracking().Where(x=>x.Printings.Any(p=>p.IsActive) && (game==null || x.Game.Code==game));
        if(!string.IsNullOrWhiteSpace(setQuery)) {var name=CatalogNormalizer.NormalizeName(setQuery);sets=sets.Where(x=>x.NormalizedName.Contains(name));}
        var total=await sets.CountAsync(ct);
        var setPage=await sets.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip(paging.Offset).Take(paging.Size).Select(x=>new CatalogMarketplaceSet(x.Id,x.Name)).ToArrayAsync(ct);
        var languages=await db.Printings.AsNoTracking().Where(x=>x.IsActive && (game==null || x.Card.Game.Code==game))
            .Select(x=>x.Language).Distinct().OrderBy(x=>x).ToArrayAsync(ct);
        var variants=await db.Variants.AsNoTracking().Where(x=>x.IsActive && x.Printing.IsActive && (game==null || x.Printing.Card.Game.Code==game))
            .Select(x=>x.Code).Distinct().OrderBy(x=>x).ToArrayAsync(ct);
        return new(setPage,paging.Page,paging.Size,total,languages,variants);
    }

    public async Task<IReadOnlyList<CatalogMarketQuotes>> GetMarketQuotes(IReadOnlyCollection<Guid> printingIds, CancellationToken ct)
    {
        if (printingIds.Count == 0) return [];
        var snapshots = await db.DailyMarketSnapshots.AsNoTracking()
            .Where(x => printingIds.Contains(x.PrintingId))
            .Where(x => !db.DailyMarketSnapshots.Any(newer => newer.PrintingId == x.PrintingId && newer.MarketDay > x.MarketDay))
            .ToArrayAsync(ct);
        var result = new List<CatalogMarketQuotes>();
        foreach (var snapshot in snapshots)
        {
            try
            {
                var stored = JsonSerializer.Deserialize<ScannerCardDetailsDto>(snapshot.Payload);
                if (stored?.Printing?.PrintingId != snapshot.PrintingId || stored.MarketQuotes is null) continue;
                var quotes = stored.MarketQuotes.Where(x => x is not null && x.MarketValueBrl > 0 && x.OriginalValue > 0
                    && x.ExchangeRate > 0 && !string.IsNullOrWhiteSpace(x.Source) && !string.IsNullOrWhiteSpace(x.OriginalCurrency)).ToArray();
                result.Add(new(snapshot.PrintingId, quotes, snapshot.RefreshAfter));
            }
            catch (JsonException e) { logger.LogWarning(e, "Invalid marketplace market snapshot for {PrintingId}", snapshot.PrintingId); }
        }
        return result;
    }
}
