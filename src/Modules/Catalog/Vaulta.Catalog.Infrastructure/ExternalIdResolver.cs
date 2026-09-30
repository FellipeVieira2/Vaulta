using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Application;

namespace Vaulta.Catalog.Infrastructure;

public sealed class ExternalIdResolver(CatalogDbContext db) : IExternalIdResolver
{
    public async Task<IReadOnlyDictionary<string, Guid>> ResolvePrintingIdsAsync(
        IReadOnlyList<string> externalIds, CancellationToken cancellationToken)
    {
        if (externalIds.Count == 0)
            return new Dictionary<string, Guid>();

        return await db.ExternalIds
            .Where(e => e.EntityType == "printing" && externalIds.Contains(e.ExternalId))
            .ToDictionaryAsync(e => e.ExternalId, e => e.EntityId, cancellationToken);
    }
}