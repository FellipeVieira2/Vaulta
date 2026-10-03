using System.Text.Json;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Domain;
namespace Vaulta.Catalog.Infrastructure;
internal static class CatalogPriceParser
{
    public static IReadOnlyList<CardMarketQuoteDto> Read(string? pricing,IReadOnlyList<Variant> variants,IReadOnlyDictionary<string,BrlExchangeRate> rates)
    {
        var result=new List<CardMarketQuoteDto>();
        foreach(var variant in variants.Where(x=>x.IsActive))
        {
            JsonElement source; string code;
            using var detailed=variant.RawValue?.TrimStart().StartsWith('{')==true ? JsonDocument.Parse(variant.RawValue) : null;
            if(detailed is not null)
            {
                var detail=detailed.RootElement;
                code=detail.TryGetProperty("type",out var type) && type.ValueKind==JsonValueKind.String ? type.GetString()! : "unknown";
                if(detail.TryGetProperty("pricing",out var specific) && specific.ValueKind==JsonValueKind.Object) source=specific.Clone();
                // A generated single variant has no separate provider identity; only this explicit case may use root pricing.
                else if(variants.Count==1 && detail.TryGetProperty("variantId",out var id) && id.GetString()=="generated" && pricing is not null)
                { using var fallback=JsonDocument.Parse(pricing); source=fallback.RootElement.Clone(); }
                else continue;
            }
            else
            {
                if(pricing is null) continue; using var legacy=JsonDocument.Parse(pricing); source=legacy.RootElement.Clone(); code=variant.Code;
            }
            using var wrapper=JsonDocument.Parse("{\"pricing\":"+source.GetRawText()+"}");
            // The selected pricing object already belongs to this exact edition; finish selects its foil/non-foil quote.
            var quotes=TcgDexScannerDetailsReader.ReadQuotes(wrapper.RootElement,[new CatalogVariantDto(variant.Id,code,variant.Name)],rates);
            result.AddRange(quotes);
        }
        return result;
    }
}
