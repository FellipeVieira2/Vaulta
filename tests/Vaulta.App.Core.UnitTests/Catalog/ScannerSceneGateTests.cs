using Vaulta.App.Core.Catalog;
using Xunit;
namespace Vaulta.App.Core.UnitTests.Catalog;
public sealed class ScannerSceneGateTests
{
    private static byte[] Frame(byte value) => Enumerable.Repeat(value, 288).ToArray();
    [Fact] public void UsefulFirstFrameCapturesWithoutOneSecondDwell()
    { Assert.True(new ScannerSceneGate().Observe(Frame(80), 0)); }
    [Fact] public void ConsumedCardAndExposureChangesDoNotRepeat()
    { var g=new ScannerSceneGate(); g.Consume(Frame(80)); Assert.False(g.Observe(Frame(86), 100)); Assert.False(g.Observe(Frame(80), 5000)); }
    [Fact] public void TwoRemovalSamplesRearmIdenticalCopyImmediately()
    { var g=new ScannerSceneGate(); g.Consume(Frame(80)); Assert.False(g.Observe(Frame(0),100,false)); Assert.False(g.Observe(Frame(0),350,false)); Assert.True(g.Observe(Frame(80),400)); }
    [Fact] public void OneMissedContourDoesNotRearmSameCard()
    { var g=new ScannerSceneGate(); g.Consume(Frame(80)); Assert.False(g.Observe(Frame(0),100,false)); Assert.False(g.Observe(Frame(80),200)); Assert.False(g.Observe(Frame(80),5000)); }
    [Fact] public void ReplacementNeedsTwoConsistentFramesRatherThanLongDwell()
    { var g=new ScannerSceneGate(); g.Consume(Frame(80)); Assert.False(g.Observe(Frame(150),100)); Assert.True(g.Observe(Frame(150),250)); }
    [Fact] public void PassingHandDoesNotRearmStationaryCard()
    { var g=new ScannerSceneGate(); g.Consume(Frame(80)); Assert.False(g.Observe(Frame(150),100)); Assert.False(g.Observe(Frame(80),250)); Assert.False(g.Observe(Frame(80),500)); }
    [Fact] public void EmptyAndAbsentFramesNeverCapture()
    { var g=new ScannerSceneGate(); Assert.False(g.Observe([],0)); Assert.False(g.Observe(Frame(0),100,false)); }
}
