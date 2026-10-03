using System.Text.RegularExpressions;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Domain;
namespace Vaulta.Vision.Application;
// Retrieval ranks candidates; visual fields authorize a canonical match. Scores are never probabilities.
public sealed class VisionPrintingResolver
{
    public const string Version="vision-evidence-v2";
    public VisionResolution Resolve(IReadOnlyList<VisionCatalogPrinting> candidates,CardEvidence? evidence)
    {
        if(evidence is null) return new("needs_better_image",null,0,null,0,"identity_not_readable");
        if(!Usable(evidence.Name,.6)) return new("needs_better_image",null,0,null,0,"name_not_readable");
        var compatible=candidates.Where(card=>Compatible(card,evidence)).DistinctBy(x=>x.Printing.PrintingId).ToArray();
        if(Usable(evidence.Language) && compatible.Any(x=>string.Equals(x.Printing.Language,evidence.Language.Value,StringComparison.OrdinalIgnoreCase)))
            compatible=compatible.Where(x=>string.Equals(x.Printing.Language,evidence.Language.Value,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(compatible.Length==0) return new(Usable(evidence.Name)?"not_in_catalog":"partial",null,0,null,0,"catalog_match_missing");
        if(compatible.Length>1) return new("ambiguous",null,0,null,0,"printing_ambiguous");
        var selected=compatible[0];
        double SafeScore(CardEvidenceField field)=>Usable(field,0)?field.Confidence:0;
        var supports=new List<double> { SafeScore(evidence.Name),SafeScore(evidence.GameCode),SafeScore(evidence.Language) };
        var distinguishing=Usable(evidence.CollectorNumber) || Usable(evidence.SetName) || Usable(evidence.Hp) && selected.Hp is not null;
        if(Usable(evidence.CollectorNumber)) supports.Add(evidence.CollectorNumber.Confidence);
        else if(Usable(evidence.SetName)) supports.Add(evidence.SetName.Confidence);
        else if(Usable(evidence.Hp)) supports.Add(evidence.Hp!.Confidence);
        var confidence=Math.Clamp(supports.Min(),0,1);
        if(!distinguishing || Usable(evidence.SetCode)) confidence=Math.Min(.79,confidence); // provider set IDs are not physical printed codes.
        if(confidence<.8) return new("partial",null,confidence,null,0,"printing_evidence_uncertain");
        if(Usable(evidence.Variant) && Usable(evidence.Finish) && evidence.Finish!.Value is "normal" or "holo" or "reverse" && evidence.Finish.Value!=evidence.Variant.Value)
            return new("identified",selected.Printing.PrintingId,confidence,null,0,"conflicting_finish");
        var surface=Usable(evidence.Variant)?evidence.Variant.Value:null;
        if(surface is null && Usable(evidence.Finish) && evidence.Finish!.Value is "normal" or "holo" or "reverse") surface=evidence.Finish.Value;
        if(surface is null) return new("identified",selected.Printing.PrintingId,confidence,null,0,"variant_not_determinable");
        var variants=selected.Variants.Where(x=>x.Surface==surface).ToArray();
        if(Usable(evidence.Edition)) variants=variants.Where(x=>x.Edition is not null && Normalize(x.Edition)==Normalize(evidence.Edition!.Value!)).ToArray();
        else if(variants.Any(x=>x.Edition is not null)) variants=[];
        if(variants.Length!=1) return new("identified",selected.Printing.PrintingId,confidence,null,0,"variant_ambiguous");
        var variantConfidence=Usable(evidence.Variant)?evidence.Variant.Confidence:evidence.Finish!.Confidence;
        if(Usable(evidence.Edition)) variantConfidence=Math.Min(variantConfidence,evidence.Edition!.Confidence);
        return new("identified",selected.Printing.PrintingId,confidence,variants[0].Id,variantConfidence,null);
    }
    private static bool Compatible(VisionCatalogPrinting card,CardEvidence e)
    {
        var p=card.Printing;
        if(Normalize(e.Name.Value!)!=Normalize(p.CardName)) return false;
        if(Usable(e.GameCode) && !string.Equals(e.GameCode.Value,p.GameCode,StringComparison.OrdinalIgnoreCase)) return false;
        if(Usable(e.Language) && !LanguageMatches(e.Language.Value!,p.Language)) return false;
        if(Usable(e.CollectorNumber) && !NumberMatches(e.CollectorNumber.Value!,p.CollectorNumber)) return false;
        if(Usable(e.SetName) && Normalize(e.SetName.Value!)!=Normalize(p.SetName) && !(card.SetNameAliases?.Any(x=>Normalize(x)==Normalize(e.SetName.Value!))??false)) return false;
        if(Usable(e.Hp) && card.Hp is not null && e.Hp!.Value!=card.Hp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) return false;
        return true;
    }
    private static string Normalize(string text)=>CatalogNormalizer.NormalizeName(text);
    public static bool Usable(CardEvidenceField? field,double minimum=.8)=>field is { Value:not null } && !string.IsNullOrWhiteSpace(field.Value) && double.IsFinite(field.Confidence) && field.Confidence>=minimum && field.Confidence<=1;
    // TCGdex uses pt for its Brazilian Portuguese catalogue; explicit other regions remain distinct.
    public static string[] LanguageAliases(string language)=>language.ToLowerInvariant() is "pt" or "pt-br" ? ["pt","pt-br"] : [language.ToLowerInvariant()];
    public static bool LanguageMatches(string seen,string stored)=>LanguageAliases(seen).Contains(stored.ToLowerInvariant());
    public static bool NumberMatches(string seen,string stored)
    {
        static string Part(string s)=>Regex.Replace(Regex.Replace(s,@"\s+","").ToUpperInvariant(),@"^(?<prefix>[A-Z]*)0+(?=\d)","${prefix}");
        var left=seen.Split('/'); var right=stored.Split('/');
        return Part(left[0])==Part(right[0]) && (left.Length==1 || right.Length==2 && Part(left[1])==Part(right[1]));
    }
}
