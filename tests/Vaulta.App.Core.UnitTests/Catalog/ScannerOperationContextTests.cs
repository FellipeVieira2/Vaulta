using Vaulta.App.Core.Catalog;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class ScannerOperationContextTests
{
    [Fact]
    public async Task OldCaptureCannotApplyAfterAReplacementAccountSession()
    {
        var session = ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var context = new ScannerOperationContext(session.Id, session.OwnerId, 1, CancellationToken.None);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var applied = false;
        var continuation = Apply(); pending.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => continuation);
        Assert.False(applied);
        async Task Apply() { await pending.Task; context.EnsureCurrent(replacement, replacement.OwnerId, 2, true); applied = true; }
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void SameAccountSessionStillRejectsPreviousActivation(long generation, bool visible)
    {
        var session = ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var context = new ScannerOperationContext(session.Id, session.OwnerId, 1, CancellationToken.None);
        Assert.Throws<OperationCanceledException>(() => context.EnsureCurrent(session, session.OwnerId, generation, visible));
    }

    [Fact]
    public void CancellationIsCheckedEvenWhenProviderReturnedSuccessfully()
    {
        var session = ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow);
        using var source = new CancellationTokenSource();
        var context = new ScannerOperationContext(session.Id, session.OwnerId, 1, source.Token);
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => context.EnsureCurrent(session, session.OwnerId, 1, true));
    }

    [Fact]
    public void CurrentOperationCanApply()
    {
        var session = ScannerSession.Start(Guid.NewGuid(), DateTimeOffset.UtcNow);
        new ScannerOperationContext(session.Id, session.OwnerId, 1, CancellationToken.None).EnsureCurrent(session, session.OwnerId, 1, true);
    }
}
