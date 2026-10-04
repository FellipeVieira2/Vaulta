using Vaulta.Catalog.Domain;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Application;

public static class VisionScanDiagnostics
{
    private static readonly string[] SafeFields=["name","collectorNumber","setName","language","gameCode","finish","variant"];
    // Whitelist rather than serializing Trace/Candidates: they can contain image hashes and signed URLs.
    public static object Export(VisionScanResultDto result)=>new
    {
        retrieval=result.Retrieval,
        evidence=result.Evidence.Where(x=>SafeFields.Contains(x.Key)).ToDictionary(x=>x.Key,x=>x.Value),
        resolution=new{result.Status,result.PrintingId,result.VariantId,result.PrintingConfidence,result.VariantConfidence,result.ReviewReason,result.ServiceIssue},
        trace=new{result.Trace.Encoder,result.Trace.IndexVersion,result.Trace.ResolverVersion,result.Trace.EvidenceModel,result.Trace.PromptVersion,result.Trace.DurationMs}
    };
    public static bool? EvidenceCompatible(VisionScanResultDto result,Catalog.Contracts.CatalogPrintingDetails? expected)
    {
        if(expected is null)return null;
        string? Read(string key)=>result.Evidence.TryGetValue(key,out var f)&& f.Confidence is >=.8 and <=1 && !string.IsNullOrWhiteSpace(f.Value)?f.Value:null;
        var name=Read("name");var number=Read("collectorNumber");var language=Read("language");var set=Read("setName");
        if(name is null || language is null || number is null && set is null)return false;
        return CatalogNormalizer.NormalizeName(name)==CatalogNormalizer.NormalizeName(expected.CardName)
            && VisionPrintingResolver.LanguageMatches(language,expected.Language)
            && (number is null || VisionPrintingResolver.NumberMatches(number,expected.CollectorNumber))
            && (set is null || CatalogNormalizer.NormalizeName(set)==CatalogNormalizer.NormalizeName(expected.SetName));
    }
    public static VisionRetrievalObservation Observation(string sample,Guid? expected,VisionScanResultDto result,Catalog.Contracts.CatalogPrintingDetails? printing=null)
    {
        bool Read(string key)=>result.Evidence.TryGetValue(key,out var f)&&f.Confidence is >=.8 and <=1 && !string.IsNullOrWhiteSpace(f.Value);
        return new(sample,expected,result.Retrieval?.Select(x=>x.PrintingId).ToArray()??[],result.PrintingId,result.Status,
            Read("name"),Read("collectorNumber"),Read("setName"),Read("language"),EvidenceCompatible(result,printing));
    }
}
