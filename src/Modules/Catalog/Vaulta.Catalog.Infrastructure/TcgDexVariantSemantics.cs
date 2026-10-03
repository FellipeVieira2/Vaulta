using System.Text.Json;
using Vaulta.Catalog.Domain;
namespace Vaulta.Catalog.Infrastructure;
// Pinned official TCGdex translations. Unknown/ambiguous labels remain unchanged.
public static class TcgDexVariantSemantics
{
    private static readonly Lazy<Dictionary<string,Dictionary<string,string?>>> Terms=new(Load);
    public static string Canonical(string group,string value)
        =>Terms.Value.TryGetValue(group,out var terms) && terms.TryGetValue(CatalogNormalizer.NormalizeName(value),out var canonical) && canonical is not null ? canonical : value;
    private static Dictionary<string,Dictionary<string,string?>> Load()
    {
        using var stream=typeof(TcgDexVariantSemantics).Assembly.GetManifestResourceStream("tcgdex-variant-localizations")
            ?? throw new InvalidDataException("TCGdex variant localization resource is missing.");
        using var document=JsonDocument.Parse(stream);
        var result=new Dictionary<string,Dictionary<string,string?>>();
        foreach(var group in document.RootElement.GetProperty("groups").EnumerateObject())
        {
            var terms=new Dictionary<string,string?>();
            foreach(var entry in group.Value.EnumerateObject())
                foreach(var label in entry.Value.EnumerateArray().Select(x=>x.GetString()!).Prepend(entry.Name))
                {
                    var token=CatalogNormalizer.NormalizeName(label);
                    if(terms.TryGetValue(token,out var previous) && previous!=entry.Name) terms[token]=null;
                    else if(!terms.ContainsKey(token)) terms[token]=entry.Name;
                }
            result[group.Name]=terms;
        }
        return result;
    }
}
