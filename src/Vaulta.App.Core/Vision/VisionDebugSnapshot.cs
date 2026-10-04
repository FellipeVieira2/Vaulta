using Vaulta.Vision.Contracts;
namespace Vaulta.App.Core.Vision;

/// <summary>Deliberately excludes captures, URLs, ownership, hashes and certificate numbers.</summary>
public static class VisionDebugSnapshot
{
    public static object Create(VisionScanResultDto result) => new
    {
        result.Status, result.PrintingId, result.VariantId, result.PrintingConfidence, result.VariantConfidence,
        result.PriceStatus, result.ReviewReason, result.Retrieval,
        evidence = result.Evidence.Where(x => x.Key is "name" or "collectorNumber" or "setName" or "language" or "gameCode" or "finish" or "variant")
            .ToDictionary(x => x.Key, x => x.Value),
        trace = new { result.Trace.Encoder, result.Trace.IndexVersion, result.Trace.ResolverVersion,
            result.Trace.EvidenceModel, result.Trace.PromptVersion, result.Trace.DurationMs }
    };
}
