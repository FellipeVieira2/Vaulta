using Vaulta.Vision.Infrastructure;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public class VisualIndexRefreshSignalTests
{
    [Fact] public async Task BurstOfChangesIsCoalescedAndChangesDuringRefreshRemainPending()
    {
        var signal=new VisualIndexRefreshSignal();
        for(var i=0;i<10;i++) signal.Request();
        Assert.True(await signal.WaitAsync(TimeSpan.FromMilliseconds(100),TimeSpan.Zero,default));
        var revision=signal.RequestedRevision;
        signal.Request();signal.Completed(revision,DateTimeOffset.UtcNow);
        Assert.True(signal.Pending);
        Assert.True(await signal.WaitAsync(TimeSpan.FromMilliseconds(100),TimeSpan.Zero,default));
        signal.Completed(signal.RequestedRevision,DateTimeOffset.UtcNow);
        Assert.False(signal.Pending);
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(10),TimeSpan.Zero,default));
    }
    [Fact] public async Task CancellationDoesNotLeaveAReaderRunning()
    {
        var signal=new VisualIndexRefreshSignal();using var ct=new CancellationTokenSource();ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>signal.WaitAsync(TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(2),ct.Token));
    }
}
