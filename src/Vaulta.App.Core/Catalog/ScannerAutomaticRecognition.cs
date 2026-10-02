using Vaulta.Catalog.Contracts;

namespace Vaulta.App.Core.Catalog;

public static class ScannerAutomaticRecognition
{
    public static CardScanCandidateDto? Select(CardScanResultDto result, Guid? previousPrintingId = null)
    {
        var candidates = result.Candidates.OrderByDescending(x => x.ConfidenceScore).ToArray();
        if (candidates.Length == 0) return null;
        var first = candidates[0];
        if (first.PrintingId == Guid.Empty || !first.HasCollectorNumberMatch || !double.IsFinite(first.ConfidenceScore)
            || first.ConfidenceScore is < .93 or > 1 || previousPrintingId is { } previous && first.PrintingId != previous)
            return null;
        return candidates.Length == 1 || first.ConfidenceScore - candidates[1].ConfidenceScore >= .12 ? first : null;
    }
}
