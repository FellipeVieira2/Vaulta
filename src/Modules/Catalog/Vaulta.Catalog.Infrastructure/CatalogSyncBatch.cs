using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Domain;

namespace Vaulta.Catalog.Infrastructure;

// One provider set is one bounded unit of tracking and identity resolution.
internal sealed class CatalogSyncBatch
{
    public required Dictionary<(string Type, string Id), CatalogExternalId> External { get; init; }
    public required Dictionary<Guid, Card> Cards { get; init; }
    public required Dictionary<Guid, Printing> Printings { get; init; }
    public static async Task<CatalogSyncBatch> LoadAsync(CatalogDbContext db,string provider,IReadOnlyList<ProviderPrinting> inputs,CancellationToken ct)
    {
        var ids=inputs.Select(x=>SourceId(provider,x)).ToArray();
        var external=await db.ExternalIds.Where(x=>x.Provider==provider && (x.EntityType=="card" || x.EntityType=="printing") && ids.Contains(x.ExternalId)).ToArrayAsync(ct);
        var cardIds=external.Where(x=>x.EntityType=="card").Select(x=>x.EntityId).ToArray();
        var printingIds=external.Where(x=>x.EntityType=="printing").Select(x=>x.EntityId).ToArray();
        return new()
        {
            External=external.ToDictionary(x=>(x.EntityType,x.ExternalId)),
            Cards=cardIds.Length==0 ? [] : await db.Cards.Where(x=>cardIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct),
            Printings=printingIds.Length==0 ? [] : await db.Printings.Include(x=>x.Variants).Where(x=>printingIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct)
        };
    }
    public static string SourceId(string provider,ProviderPrinting input)=>provider=="tcgdex" && CatalogNormalizer.NormalizeLanguage(input.Language)!="en"
        ? $"{CatalogNormalizer.NormalizeLanguage(input.Language)}:{input.ExternalId}" : input.ExternalId;
}