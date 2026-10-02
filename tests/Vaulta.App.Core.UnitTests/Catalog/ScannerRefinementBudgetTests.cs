using Vaulta.App.Core.Catalog;
using Vaulta.Catalog.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class ScannerRefinementBudgetTests
{
    [Fact]
    public void SafeFirstReadingNeverRefines()
    {
        var result = new CardScanResultDto([new(Guid.NewGuid(), "Pikachu", "Set", "58", null, null, null, null, [], .95, HasCollectorNumberMatch: true)]);
        Assert.False(new ScannerRefinementBudget().TryRefine(result));
    }
    [Fact]
    public void UncertainSceneHasAtMostOneRefinement()
    {
        var budget = new ScannerRefinementBudget();
        Assert.True(budget.TryRefine(new([])));
        for (var i = 0; i < 10000; i++) Assert.False(budget.TryRefine(new([])));
    }
    [Fact]
    public void StableConsumedSceneDoesNotScheduleAdditionalScans()
    {
        var gate = new ScannerSceneGate(); var scene = new byte[288];
        Assert.False(gate.Observe(scene, 0)); Assert.True(gate.Observe(scene, 1200)); gate.Consume(scene);
        for (var i = 0; i < 10000; i++) Assert.False(gate.Observe(scene, 1800 + i * 600));
    }
}
