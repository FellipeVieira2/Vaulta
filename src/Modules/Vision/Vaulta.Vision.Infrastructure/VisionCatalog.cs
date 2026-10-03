using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Domain;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Vision.Application;
namespace Vaulta.Vision.Infrastructure;
// Canonical identity and candidate metadata come only from the local Vaulta database.
public sealed class VisionCatalog(CatalogDbContext db) : IVisionCatalog
{
    public async Task<IReadOnlyList<VisionCatalogPrinting>> GetPrintingsAsync(IReadOnlyList<Guid> ids,CancellationToken ct)
    {
        if(ids.Count>100) throw new ArgumentException("Candidate ID limit exceeded.");
        return await ReadAsync(Query().Where(x=>ids.Contains(x.Id)),ct);
    }
    public async Task<IReadOnlyList<VisionCatalogPrinting>> FindEvidenceCandidatesAsync(CardEvidence evidence,CancellationToken ct)
    {
        if(!VisionPrintingResolver.Usable(evidence.Name,.6)) return [];
        var normalized=CatalogNormalizer.NormalizeName(evidence.Name.Value!);
        var query=Query().Where(x=>x.Card.NormalizedName==normalized);
        if(VisionPrintingResolver.Usable(evidence.GameCode)) query=query.Where(x=>x.Card.Game.Code==evidence.GameCode.Value);
        if(VisionPrintingResolver.Usable(evidence.Language))
        {
            var languages=VisionPrintingResolver.LanguageAliases(evidence.Language.Value!);
            query=query.Where(x=>languages.Contains(x.Language.ToLower()));
        }
        if(VisionPrintingResolver.Usable(evidence.CollectorNumber))
        {
            var parts=evidence.CollectorNumber.Value!.Split('/');
            static string NumberPart(string value)=>System.Text.RegularExpressions.Regex.Replace(CatalogNormalizer.NormalizeCollectorNumber(value),@"^([A-Z]*)0+(?=\d)","$1");
            var first=NumberPart(parts[0]);
            // Same equivalence as the resolver, applied before candidate limiting in PostgreSQL.
            var numbered=db.Printings.FromSqlInterpolated($"SELECT * FROM catalog.printings WHERE regexp_replace(split_part(normalized_collector_number,'/',1),'^([A-Z]*)0+(?=[0-9])','\\1')={first}");
            if(parts.Length==2)
            {
                var total=NumberPart(parts[1]);
                numbered=db.Printings.FromSqlInterpolated($"SELECT * FROM catalog.printings WHERE regexp_replace(split_part(normalized_collector_number,'/',1),'^([A-Z]*)0+(?=[0-9])','\\1')={first} AND regexp_replace(split_part(normalized_collector_number,'/',2),'^([A-Z]*)0+(?=[0-9])','\\1')={total}");
            }
            query=query.Where(x=>numbered.Select(n=>n.Id).Contains(x.Id));
        }
        if(VisionPrintingResolver.Usable(evidence.SetName)) { var set=CatalogNormalizer.NormalizeName(evidence.SetName.Value!);query=query.Where(x=>x.Set.NormalizedName==set || db.ExternalIds.Any(a=>a.Provider=="tcgdex" && a.EntityType=="set-name" && a.EntityId==x.SetId && a.ExternalId==x.SetId.ToString()+":"+set)); }
        return await ReadAsync(query,ct,true);
    }
    private IQueryable<Printing> Query()=>db.Printings.AsNoTracking().Where(x=>x.IsActive)
        .Include(x=>x.Card).ThenInclude(x=>x.Game).Include(x=>x.Set).Include(x=>x.Variants.Where(v=>v.IsActive));
    private async Task<IReadOnlyList<VisionCatalogPrinting>> ReadAsync(IQueryable<Printing> query,CancellationToken ct,bool rejectOverflow=false)
    {
        var rows=await query.OrderBy(x=>x.Id).Take(rejectOverflow?101:100).ToArrayAsync(ct);
        if(rejectOverflow && rows.Length>100) throw new VisionEvidenceCandidateLimitException();
        var setIds=rows.Select(x=>x.SetId).Distinct().ToArray();
        var aliases=await db.ExternalIds.AsNoTracking().Where(x=>x.Provider=="tcgdex" && x.EntityType=="set-name" && setIds.Contains(x.EntityId))
            .Select(x=>new {x.EntityId,x.ExternalId}).ToArrayAsync(ct);
        var names=aliases.Where(x=>x.ExternalId.StartsWith(x.EntityId.ToString("D")+":",StringComparison.Ordinal))
            .GroupBy(x=>x.EntityId).ToDictionary(x=>x.Key,x=>x.Select(a=>a.ExternalId[37..]).ToArray());
        var result=new List<VisionCatalogPrinting>();
        foreach(var row in rows)
        {
            using var metadata=JsonDocument.Parse(row.MetadataJson??"{}"); var root=metadata.RootElement;
            string? Text(string key)=>root.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String?v.GetString():null;
            int? hp=root.TryGetProperty("hp",out var life) && life.ValueKind==JsonValueKind.Number && life.TryGetInt32(out var points)?points:null;
            var variants=row.Variants.OrderBy(x=>x.Id).Select(MapVariant).ToArray();
            var printing=new CatalogPrintingDetails(row.Id,row.CardId,row.SetId,row.Card.Game.Code,row.Set.Name,row.Card.Name,row.CollectorNumber,row.Language,row.Rarity,
                row.ArtworkAssetId is null?null:$"/api/v1/catalog/printings/{row.Id}/artwork",row.Variants.OrderBy(x=>x.Id).Select(x=>new CatalogVariantDto(x.Id,x.Code,x.Name)).ToArray());
            result.Add(new(printing,hp,Text("stage"),Text("category"),variants,names.GetValueOrDefault(row.SetId)));
        }
        return result;
    }
    private static VisionCatalogVariant MapVariant(Variant row)
    {
        string? type=null; string? subtype=null; string[] stamps=[];
        if(row.RawValue?.StartsWith('{')==true)
        {
            using var raw=JsonDocument.Parse(row.RawValue); var root=raw.RootElement;
            if(root.TryGetProperty("type",out var t) && t.ValueKind==JsonValueKind.String) type=TcgDexVariantSemantics.Canonical("type",t.GetString()!);
            if(root.TryGetProperty("subtype",out var s) && s.ValueKind==JsonValueKind.String) subtype=TcgDexVariantSemantics.Canonical("subtype",s.GetString()!);
            if(root.TryGetProperty("stamp",out var stamp) && stamp.ValueKind==JsonValueKind.Array) stamps=stamp.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>TcgDexVariantSemantics.Canonical("stamp",x.GetString()!)).ToArray();
        }
        type??=row.Code.Split('-')[0];
        var surface=type switch { "normal" or "nonHolo" or "non-holo"=>"normal","holo" or "foil"=>"holo","reverse" or "reverseHolo"=>"reverse",_=>null };
        var edition=new List<string>(); if(subtype is not null && subtype!="normal") edition.Add(subtype);
        edition.AddRange(stamps.Select(x=>x=="1st-edition"?"first-edition":x));
        return new(row.Id,row.Code,row.Name,surface,edition.Count==0?null:string.Join('-',edition));
    }
}
