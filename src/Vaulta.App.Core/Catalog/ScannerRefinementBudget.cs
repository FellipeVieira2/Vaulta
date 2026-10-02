using Vaulta.Catalog.Contracts;

namespace Vaulta.App.Core.Catalog;

/// <summary>One initial capture and at most one refinement for an unchanged scene.</summary>
public sealed class ScannerRefinementBudget
{
    public const int MaximumRefinements = 1;
    private int _refinements;
    public bool TryRefine(CardScanResultDto result)
    {
        if (ScannerAutomaticRecognition.Select(result) is not null || _refinements >= MaximumRefinements) return false;
        _refinements++;
        return true;
    }
}
