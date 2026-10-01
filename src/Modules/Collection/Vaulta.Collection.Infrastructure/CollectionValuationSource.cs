using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Collection.Application;
using Vaulta.Collection.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Collection.Infrastructure;

internal sealed class CollectionValuationSource(CollectionDbContext db, IScannerCardDetailsReader pricing) : ICollectionValuationSource
{
    public async Task<IReadOnlyList<CollectionValuationPosition>> GetPositions(Guid owner, Guid? entryId, CancellationToken ct)
    {
        if (entryId is { } selected && !await db.Entries.AsNoTracking().AnyAsync(x => x.UserId == owner && x.Id == selected, ct))
            throw new NotFoundException("Collection entry not found.");
        var owned = from item in db.Items.AsNoTracking()
                    join entry in db.Entries.AsNoTracking() on item.CollectionEntryId equals entry.Id
                    where item.UserId == owner && entry.UserId == owner && item.Status == CollectionRules.ActiveStatus
                        && (!entryId.HasValue || entry.Id == entryId.Value)
                    group item by new { entry.Id, entry.PrintingId, entry.VariantId } into position
                    orderby position.Key.PrintingId, position.Key.Id
                    select new CollectionValuationPosition(position.Key.Id, position.Key.PrintingId, position.Key.VariantId, position.Count());
        return await owned.ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<CardMarketQuoteDto>> GetQuotes(Guid printingId, CancellationToken ct) =>
        (await pricing.GetAsync(printingId, ct))?.MarketQuotes ?? [];
}
