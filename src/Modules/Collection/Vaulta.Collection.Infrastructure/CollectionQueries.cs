using Microsoft.EntityFrameworkCore;
using Vaulta.Assets.Application;
using Vaulta.Assets.Contracts;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Collection.Application;
using Vaulta.Collection.Contracts;
using Vaulta.Collection.Domain;

namespace Vaulta.Collection.Infrastructure;

internal sealed class CollectionQueries(CollectionDbContext db, ICatalogCollectionReader catalog, IAssetService assets) : ICollectionQueries
{
    public async Task<CollectionPageDto> GetMyCollection(Guid userId, CollectionQuery query, CancellationToken cancellationToken)
    {
        var filter = ValidateQuery(query);
        var matchingPrintings = await catalog.SearchPrintingIds(filter.Query, filter.Game, filter.SetId, cancellationToken);
        var filtered = db.Entries.AsNoTracking().Where(entry => entry.UserId == userId && matchingPrintings.Contains(entry.PrintingId) &&
            db.Items.Any(item => item.CollectionEntryId == entry.Id && item.UserId == userId && item.Status == CollectionRules.ActiveStatus));
        if (filter.VariantId is { } variantId) filtered = filtered.Where(x => x.VariantId == variantId);
        if (filter.Condition is { Length: > 0 } condition)
            filtered = filtered.Where(entry => db.Items.Any(item => item.CollectionEntryId == entry.Id && item.UserId == userId && item.Status == CollectionRules.ActiveStatus && item.Condition == condition));

        var total = await filtered.CountAsync(cancellationToken);
        var selectedIds = filter.Sort switch
        {
            "quantity" => await filtered.Select(entry => new { entry.Id, Count = db.Items.Count(item => item.CollectionEntryId == entry.Id && item.UserId == userId && item.Status == CollectionRules.ActiveStatus) })
                .OrderByDescending(x => x.Count).ThenBy(x => x.Id).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).Select(x => x.Id).ToArrayAsync(cancellationToken),
            "recent" => await filtered.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).Select(x => x.Id).ToArrayAsync(cancellationToken),
            _ => await filtered.OrderBy(x => x.PrintingId).ThenBy(x => x.VariantId).ThenBy(x => x.Id).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).Select(x => x.Id).ToArrayAsync(cancellationToken)
        };
        if (selectedIds.Length == 0) return new CollectionPageDto([], filter.Page, filter.PageSize, total);

        var entries = await db.Entries.AsNoTracking().Where(x => selectedIds.Contains(x.Id))
            .Select(entry => new { Entry = entry, Quantity = db.Items.Count(item => item.CollectionEntryId == entry.Id && item.UserId == userId && item.Status == CollectionRules.ActiveStatus),
                Conditions = db.Items.Where(item => item.CollectionEntryId == entry.Id && item.UserId == userId && item.Status == CollectionRules.ActiveStatus)
                    .GroupBy(item => item.Condition).Select(group => new { group.Key, Count = group.Count() }).ToArray() }).ToArrayAsync(cancellationToken);
        var printingData = await catalog.GetPrintings(entries.Select(x => x.Entry.PrintingId).Distinct().ToArray(), cancellationToken);
        var variantData = await catalog.GetVariants(entries.Where(x => x.Entry.VariantId.HasValue).Select(x => x.Entry.VariantId!.Value).Distinct().ToArray(), cancellationToken);
        var printingMap = printingData.ToDictionary(x => x.PrintingId);
        var variantMap = variantData.ToDictionary(x => x.VariantId);
        var output = selectedIds.Select(id => entries.Single(row => row.Entry.Id == id)).Where(row => printingMap.ContainsKey(row.Entry.PrintingId)).Select(row =>
        {
            var printing = printingMap[row.Entry.PrintingId];
            var variant = row.Entry.VariantId is { } id && variantMap.TryGetValue(id, out var found) ? found : null;
            return new CollectionEntryListItemDto(row.Entry.Id, row.Entry.PrintingId, row.Entry.VariantId, printing.GameCode, printing.CardName,
                printing.SetName, printing.CollectorNumber, printing.Language, printing.Rarity, printing.ArtworkUrl, variant?.Code, row.Quantity,
                row.Conditions.ToDictionary(x => x.Key, x => x.Count));
        }).ToArray();
        if (filter.Sort == "name") output = output.OrderBy(x => x.CardName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.SetName, StringComparer.OrdinalIgnoreCase).ToArray();
        return new CollectionPageDto(output, filter.Page, filter.PageSize, total);
    }

    public async Task<CollectionEntryDetailsDto?> GetEntry(Guid userId, Guid entryId, int page, int pageSize, CancellationToken cancellationToken)
    {
        ValidatePagination(page, pageSize);
        var entry = await db.Entries.AsNoTracking().Where(x => x.Id == entryId && x.UserId == userId).Select(x => new { x.Id, x.PrintingId, x.VariantId }).SingleOrDefaultAsync(cancellationToken);
        if (entry is null) return null;
        var printing = await catalog.GetPrinting(entry.PrintingId, cancellationToken);
        if (printing is null) return null;
        var activeItems = db.Items.AsNoTracking().Where(x => x.CollectionEntryId == entryId && x.UserId == userId && x.Status == CollectionRules.ActiveStatus);
        var total = await activeItems.CountAsync(cancellationToken);
        var conditions = await activeItems.GroupBy(x => x.Condition).Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var ids = await activeItems.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var itemDtos = await GetItems(userId, ids, cancellationToken);
        var variant = entry.VariantId is { } variantId ? await catalog.GetVariant(variantId, cancellationToken) : null;
        return new CollectionEntryDetailsDto(entry.Id, printing.PrintingId, entry.VariantId, printing.GameCode, printing.CardName, printing.SetName, printing.CollectorNumber,
            printing.Language, printing.Rarity, printing.ArtworkUrl, variant?.Code, total, conditions, itemDtos, page, pageSize, total);
    }

    public async Task<CollectibleItemDto?> GetItem(Guid userId, Guid itemId, CancellationToken cancellationToken) =>
        (await GetItems(userId, [itemId], cancellationToken)).SingleOrDefault();

    public async Task<CollectionSummaryDto> GetSummary(Guid userId, CancellationToken cancellationToken)
    {
        var activeItems = db.Items.AsNoTracking().Where(x => x.UserId == userId && x.Status == CollectionRules.ActiveStatus);
        var totalItems = await activeItems.CountAsync(cancellationToken);
        var conditions = await activeItems.GroupBy(x => x.Condition).Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var byEntry = await activeItems.GroupBy(x => x.CollectionEntryId).Select(group => new { EntryId = group.Key, Count = group.Count() }).ToArrayAsync(cancellationToken);
        if (byEntry.Length == 0) return new CollectionSummaryDto(0, 0, new Dictionary<string, int>(), conditions);
        var entries = await db.Entries.AsNoTracking().Where(x => x.UserId == userId && byEntry.Select(y => y.EntryId).Contains(x.Id))
            .Select(x => new { x.Id, x.PrintingId }).ToArrayAsync(cancellationToken);
        var printingData = await catalog.GetPrintings(entries.Select(x => x.PrintingId).Distinct().ToArray(), cancellationToken);
        var games = printingData.ToDictionary(x => x.PrintingId, x => x.GameCode);
        var counts = byEntry.ToDictionary(x => x.EntryId, x => x.Count);
        var itemsByGame = entries.Where(x => games.ContainsKey(x.PrintingId)).GroupBy(x => games[x.PrintingId])
            .ToDictionary(group => group.Key, group => group.Sum(x => counts[x.Id]), StringComparer.Ordinal);
        return new CollectionSummaryDto(byEntry.Length, totalItems, itemsByGame, conditions);
    }

    private async Task<IReadOnlyList<CollectibleItemDto>> GetItems(Guid userId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0) return [];
        var rows = await db.Items.AsNoTracking().Where(x => itemIds.Contains(x.Id) && x.UserId == userId && x.Status == CollectionRules.ActiveStatus)
            .Select(x => new { x.Id, x.CollectionEntryId, x.Condition, x.AcquisitionAmount, x.AcquisitionCurrency, x.AcquisitionDate, x.Notes, x.Status, x.CreatedAt, x.UpdatedAt, x.Version,
                Assets = x.Assets.OrderBy(asset => asset.SortOrder).Select(asset => new { asset.AssetId, asset.Type, asset.SortOrder, asset.IsPrimary }).ToArray() }).ToArrayAsync(cancellationToken);
        var result = new List<CollectibleItemDto>(rows.Length);
        foreach (var row in rows)
        {
            var assetDtos = new List<CollectionAssetDto>(row.Assets.Length);
            foreach (var asset in row.Assets)
            {
                var signed = await assets.CreatePrivateReadUrl(userId, asset.AssetId, cancellationToken);
                if (signed is not null) assetDtos.Add(new CollectionAssetDto(asset.AssetId, asset.Type, asset.SortOrder, asset.IsPrimary, signed.Url, signed.ExpiresAt));
            }
            var price = row.AcquisitionAmount is not null && row.AcquisitionCurrency is not null ? new AcquisitionPrice(row.AcquisitionAmount.Value, row.AcquisitionCurrency) : null;
            result.Add(new CollectibleItemDto(row.Id, row.CollectionEntryId, row.Condition, price, row.AcquisitionDate, row.Notes, row.Status, row.CreatedAt, row.UpdatedAt, row.Version, assetDtos));
        }
        return result;
    }

    private static CollectionQuery ValidateQuery(CollectionQuery query)
    {
        ValidatePagination(query.Page, query.PageSize);
        if (query.Sort is not ("recent" or "name" or "quantity")) throw new Vaulta.SharedKernel.DomainException("Unsupported collection sort.");
        return query with { Condition = query.Condition?.ToUpperInvariant(), Game = query.Game?.ToLowerInvariant() };
    }

    private static void ValidatePagination(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new Vaulta.SharedKernel.DomainException("Invalid pagination values.");
    }
}
