using Vaulta.App.Core.Catalog;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class ScannerSceneGateTests
{
    [Fact]
    public void CaptureRequiresOneFullSecondAndConsumedPhotoDoesNotRequestAnother()
    {
        var gate = new ScannerSceneGate(); var card = Enumerable.Repeat((byte)80, 288).ToArray();
        Assert.False(gate.Observe(card, 0, cardPresent: true));
        Assert.False(gate.Observe(card, 999, cardPresent: true));
        Assert.True(gate.Observe(card, 1000, cardPresent: true));
        gate.Consume(card);
        Assert.False(gate.Observe(card, 5000, cardPresent: true));
        Assert.False(gate.Observe(new byte[288], 6000, cardPresent: false));
    }

    [Fact]
    public void StationaryCardAndSmallExposureChangesDoNotRepeatARequest()
    {
        var gate = new ScannerSceneGate(); var card = Enumerable.Repeat((byte)80, 288).ToArray();
        Assert.False(gate.Observe(card, 0)); Assert.True(gate.Observe(card, 1200)); gate.Consume(card);
        Assert.False(gate.Observe(card, 2400));
        Assert.False(gate.Observe(Enumerable.Repeat((byte)86, 288).ToArray(), 6000));
        Assert.False(gate.Observe(card, 10000));
    }

    [Fact]
    public void NextCardMustSettleAndAnotherIdenticalCopyRequiresVisibleRemoval()
    {
        var gate = new ScannerSceneGate(); var first = new byte[288]; var next = Enumerable.Repeat((byte)100, 288).ToArray();
        gate.Observe(first, 0); gate.Consume(first);
        Assert.False(gate.Observe(next, 2000)); Assert.False(gate.Observe(next, 2600));
        Assert.True(gate.Observe(next, 3200)); gate.Consume(next);
        Assert.False(gate.Observe(next, 7000));
        Assert.False(gate.Observe(first, 7500)); // hand/removal passes over the stack
        Assert.False(gate.Observe(next, 8100));
        Assert.True(gate.Observe(next, 9300));
        Assert.False(ScannerSceneGate.IsSameScene(first, next));
    }
}
