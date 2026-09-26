using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Infrastructure;

internal sealed class CatalogQueries(CatalogDbContext db) : ICatalogSearch, ICatalogCollectionReader
{
    public async Task<CatalogSearchPage> Search(string query, string? gameCode, int page, int pageSize, CancellationToken cancellationToken)
    {
        var normalized = Catalog.Domain.CatalogNormalizer.NormalizeName(query);
        if (normalized.Length == 0) return new([], page, pageSize, 0);
        var results = db.Printings.AsNoTracking().Where(x => x.Card.NormalizedName.Contains(normalized));
        if (!string.IsNullOrWhiteSpace(gameCode)) results = results.Where(x => x.Card.Game.Code == gameCode.ToLowerInvariant());
        var total = await results.CountAsync(cancellationToken);
        var items = await results.OrderByDescending(x => x.Card.NormalizedName == normalized)
            .ThenBy(x => x.Card.NormalizedName).ThenBy(x => x.Set.Name).ThenBy(x => x.NormalizedCollectorNumber).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new CatalogSearchResult(x.Id, x.Card.Game.Code, x.SetId, x.Set.Name, x.Card.Name, x.CollectorNumber, x.Language, x.Rarity, x.ExternalArtworkUrl))
            .ToArrayAsync(cancellationToken);
        return new(items, page, pageSize, total);
    }

    public Task<CatalogPrintingDetails?> GetPrinting(Guid id, CancellationToken cancellationToken) => db.Printings.AsNoTracking()
        .Where(x => x.Id == id)
        .Select(x => new CatalogPrintingDetails(x.Id, x.CardId, x.SetId, x.Card.Game.Code, x.Set.Name, x.Card.Name, x.CollectorNumber, x.Language, x.Rarity, x.ExternalArtworkUrl,
            x.Variants.Where(v => v.IsActive).OrderBy(v => v.Code).Select(v => new CatalogVariantDto(v.Id, v.Code, v.Name)).ToArray()))
        .SingleOrDefaultAsync(cancellationToken);

    Task<CollectionPrintingDetails?> ICatalogCollectionReader.GetPrinting(Guid printingId, CancellationToken cancellationToken) => db.Printings.AsNoTracking()
        .Where(x => x.Id == printingId)
        .Select(x => new CollectionPrintingDetails(x.Id, x.CardId, x.SetId, x.Card.Game.Code, x.Set.Name, x.Card.Name, x.CollectorNumber, x.Language, x.Rarity, x.ExternalArtworkUrl))
        .SingleOrDefaultAsync(cancellationToken);

    async Task<IReadOnlyList<CollectionPrintingDetails>> ICatalogCollectionReader.GetPrintings(IReadOnlyCollection<Guid> printingIds, CancellationToken cancellationToken) =>
        await db.Printings.AsNoTracking().Where(x => printingIds.Contains(x.Id))
            .Select(x => new CollectionPrintingDetails(x.Id, x.CardId, x.SetId, x.Card.Game.Code, x.Set.Name, x.Card.Name, x.CollectorNumber, x.Language, x.Rarity, x.ExternalArtworkUrl))
            .ToArrayAsync(cancellationToken);

    async Task<IReadOnlyList<CollectionVariantDetails>> ICatalogCollectionReader.GetVariants(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken) =>
        await db.Variants.AsNoTracking().Where(x => variantIds.Contains(x.Id))
            .Select(x => new CollectionVariantDetails(x.Id, x.PrintingId, x.Code, x.Name)).ToArrayAsync(cancellationToken);

    async Task<IReadOnlyList<Guid>> ICatalogCollectionReader.SearchPrintingIds(string? query, string? gameCode, Guid? setId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(gameCode) && setId is null) return [];
        var printings = db.Printings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = Vaulta.Catalog.Domain.CatalogNormalizer.NormalizeName(query);
            if (normalized.Length == 0) return [];
            printings = printings.Where(x => x.Card.NormalizedName.Contains(normalized));
        }
        if (!string.IsNullOrWhiteSpace(gameCode)) printings = printings.Where(x => x.Card.Game.Code == gameCode.ToLowerInvariant());
        if (setId is { } id) printings = printings.Where(x => x.SetId == id);
        return await printings.Select(x => x.Id).ToArrayAsync(cancellationToken);
    }

    public Task<CollectionVariantDetails?> GetVariant(Guid variantId, CancellationToken cancellationToken) => db.Variants.AsNoTracking()
        .Where(x => x.Id == variantId)
        .Select(x => new CollectionVariantDetails(x.Id, x.PrintingId, x.Code, x.Name))
        .SingleOrDefaultAsync(cancellationToken);
}
