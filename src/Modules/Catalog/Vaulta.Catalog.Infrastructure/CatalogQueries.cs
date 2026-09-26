using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Infrastructure;

internal sealed class CatalogQueries(CatalogDbContext db) : ICatalogSearch, ICatalogCollectionReader
{
    public async Task<IReadOnlyList<CatalogSearchResult>> Search(string query, string? gameCode, int page, int pageSize, CancellationToken cancellationToken)
    {
        var normalized = Catalog.Domain.CatalogNormalizer.NormalizeName(query);
        if (normalized.Length == 0) return [];
        var results = db.Printings.AsNoTracking().Where(x => x.Card.NormalizedName.Contains(normalized));
        if (!string.IsNullOrWhiteSpace(gameCode)) results = results.Where(x => x.Card.Game.Code == gameCode.ToLowerInvariant());
        return await results.OrderByDescending(x => x.Card.NormalizedName == normalized)
            .ThenBy(x => x.Card.NormalizedName).ThenBy(x => x.Set.Name).ThenBy(x => x.NormalizedCollectorNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new CatalogSearchResult(x.Id, x.Card.Game.Code, x.Set.Name, x.Card.Name, x.CollectorNumber, x.Language, x.Rarity))
            .ToArrayAsync(cancellationToken);
    }

    public Task<CatalogPrintingDetails?> GetPrinting(Guid id, CancellationToken cancellationToken) => db.Printings.AsNoTracking()
        .Where(x => x.Id == id)
        .Select(x => new CatalogPrintingDetails(x.Id, x.Card.Game.Code, x.Set.Name, x.Card.Name, x.CollectorNumber, x.Language, x.Rarity,
            x.Variants.OrderBy(v => v.Code).Select(v => v.Name).ToArray()))
        .SingleOrDefaultAsync(cancellationToken);

    Task<CollectionPrintingDetails?> ICatalogCollectionReader.GetPrinting(Guid printingId, CancellationToken cancellationToken) => db.Printings.AsNoTracking()
        .Where(x => x.Id == printingId)
        .Select(x => new CollectionPrintingDetails(x.Id, x.CardId, x.SetId, x.Card.Game.Code, x.Set.Name, x.Card.Name, x.CollectorNumber, x.Language, x.Rarity, x.Card.ImageAssetKey))
        .SingleOrDefaultAsync(cancellationToken);

    async Task<IReadOnlyList<CollectionPrintingDetails>> ICatalogCollectionReader.GetPrintings(IReadOnlyCollection<Guid> printingIds, CancellationToken cancellationToken) =>
        await db.Printings.AsNoTracking().Where(x => printingIds.Contains(x.Id))
            .Select(x => new CollectionPrintingDetails(x.Id, x.CardId, x.SetId, x.Card.Game.Code, x.Set.Name, x.Card.Name, x.CollectorNumber, x.Language, x.Rarity, x.Card.ImageAssetKey))
            .ToArrayAsync(cancellationToken);

    async Task<IReadOnlyList<CollectionVariantDetails>> ICatalogCollectionReader.GetVariants(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken) =>
        await db.Variants.AsNoTracking().Where(x => variantIds.Contains(x.Id))
            .Select(x => new CollectionVariantDetails(x.Id, x.PrintingId, x.Code, x.Name)).ToArrayAsync(cancellationToken);

    async Task<IReadOnlyList<Guid>> ICatalogCollectionReader.SearchPrintingIds(string? query, string? gameCode, Guid? setId, CancellationToken cancellationToken)
    {
        var printings = db.Printings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = Vaulta.Catalog.Domain.CatalogNormalizer.NormalizeName(query);
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
