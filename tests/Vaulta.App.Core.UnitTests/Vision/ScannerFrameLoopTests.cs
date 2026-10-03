using Vaulta.App.Core.Catalog;
using Xunit;
namespace Vaulta.App.Core.UnitTests.Vision;
public class ScannerFrameLoopTests
{
    private static byte[] Frame(byte value=80)=>Enumerable.Repeat(value,288).ToArray();
    [Fact] public void UsefulFrontStartsOneRequestUntilCompleted()
    { var loop=new ScannerFrameLoop(); Assert.True(loop.TryBegin(Frame(),0,ScannerFrameKind.Front,true)); Assert.False(loop.TryBegin(Frame(150),100,ScannerFrameKind.Front,true)); loop.Complete(); Assert.True(loop.TryBegin(Frame(150),250,ScannerFrameKind.Front,true)); }
    [Fact] public void BackEmptyAndBlurNeverStartRecognition()
    { var loop=new ScannerFrameLoop(); Assert.False(loop.TryBegin(Frame(),0,ScannerFrameKind.Back,true)); Assert.False(loop.TryBegin(Frame(),100,ScannerFrameKind.NoCard,true)); Assert.False(loop.TryBegin(Frame(),200,ScannerFrameKind.Front,false)); Assert.True(loop.TryBegin(Frame(),300,ScannerFrameKind.Unknown,true)); }
    [Fact] public void RemovalDuringRequestRearmsAnIdenticalPhysicalCopy()
    { var loop=new ScannerFrameLoop(); Assert.True(loop.TryBegin(Frame(),0,ScannerFrameKind.Front,true)); Assert.False(loop.TryBegin(Frame(0),100,ScannerFrameKind.NoCard,false)); Assert.False(loop.TryBegin(Frame(0),350,ScannerFrameKind.NoCard,false)); loop.Complete(); Assert.True(loop.TryBegin(Frame(),400,ScannerFrameKind.Front,true)); }
}
