using Vaulta.App.Core.Identity;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Identity;

public sealed class AuthenticationCommitCoordinatorTests
{
    [Fact]
    public async Task CancelledStorageFinishesAndRollsBackBeforeNewLoginCommits()
    {
        var coordinator = new AuthenticationCommitCoordinator();
        var storageRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelled = new CancellationTokenSource();
        string? stored = null, user = null;
        var changes = new List<string>();
        var generationA = coordinator.BeginChange();
        var commitA = coordinator.TryCommitAsync(generationA, async _ =>
        {
            await storageRelease.Task; stored = "A"; changes.Add("save A");
        }, () => { user = "A"; changes.Add("publish A"); }, Clear, cancelled.Token);
        cancelled.Cancel();
        var generationB = coordinator.BeginChange();
        var commitB = coordinator.TryCommitAsync(generationB, _ =>
        {
            stored = "B"; changes.Add("save B"); return Task.CompletedTask;
        }, () => user = "B", Clear);
        Assert.False(commitB.IsCompleted);
        storageRelease.SetResult();
        Assert.False(await commitA);
        Assert.True(await commitB);
        Assert.Equal("B", stored);
        Assert.Equal("B", user);
        Assert.Equal(new[] { "save A", "clear", "save B" }, changes);
        Task Clear() { stored = null; user = null; changes.Add("clear"); return Task.CompletedTask; }
    }

    [Fact]
    public async Task LogoutInvalidatesInFlightStorageAndNeverPublishesItsUser()
    {
        var coordinator = new AuthenticationCommitCoordinator();
        var storageRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? stored = null, user = "previous";
        var generationA = coordinator.BeginChange();
        var publishedA = false;
        var commitA = coordinator.TryCommitAsync(generationA, async _ => { await storageRelease.Task; stored = "A"; },
            () => { publishedA = true; user = "A"; }, Clear);
        var logout = coordinator.BeginChange(() => user = null);
        Assert.Null(user);
        var clearing = coordinator.InvalidateAsync(logout, Clear);
        storageRelease.SetResult();
        Assert.False(await commitA);
        Assert.True(await clearing);
        Assert.False(publishedA);
        Assert.Null(stored);
        Assert.Null(user);
        Task Clear() { stored = null; user = null; return Task.CompletedTask; }
    }

    [Fact]
    public async Task DelayedLogoutDoesNotClearANewerCommittedLogin()
    {
        var coordinator = new AuthenticationCommitCoordinator();
        var logout = coordinator.BeginChange();
        var login = coordinator.BeginChange();
        string? stored = null, user = null;
        Assert.True(await coordinator.TryCommitAsync(login, _ => { stored = "B"; return Task.CompletedTask; },
            () => user = "B", Clear));
        Assert.False(await coordinator.InvalidateAsync(logout, Clear));
        Assert.Equal("B", stored);
        Assert.Equal("B", user);
        Task Clear() { stored = null; user = null; return Task.CompletedTask; }
    }

    [Fact]
    public async Task SupersededResponseCannotStartStorageOrPublish()
    {
        var coordinator = new AuthenticationCommitCoordinator();
        var old = coordinator.BeginChange();
        coordinator.BeginChange();
        var writes = 0;
        Assert.False(await coordinator.TryCommitAsync(old, _ => { writes++; return Task.CompletedTask; },
            () => writes++, () => { writes++; return Task.CompletedTask; }));
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task RefreshSnapshotCannotCommitAfterLogoutBegins()
    {
        var coordinator = new AuthenticationCommitCoordinator();
        var snapshot = await coordinator.ReadAsync<string>(_ => Task.FromResult<string?>("stored refresh token"));
        Assert.NotNull(snapshot);
        coordinator.BeginChange();
        var writes = 0;
        Assert.False(await coordinator.TryCommitAsync(snapshot.Generation, _ => { writes++; return Task.CompletedTask; },
            () => writes++, () => Task.CompletedTask));
        Assert.False(await coordinator.TryPublishAsync(snapshot.Generation, () => writes++));
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task SessionReadsAreBlockedUntilPendingChangeEstablishesItsStorage()
    {
        var coordinator = new AuthenticationCommitCoordinator();
        var readCalls = 0;
        var pending = coordinator.BeginChange();
        Assert.Null(await coordinator.ReadAsync<string>(_ => { readCalls++; return Task.FromResult<string?>("old"); }));
        Assert.Equal(0, readCalls);
        await coordinator.InvalidateAsync(pending, () => Task.CompletedTask);
        var snapshot = await coordinator.ReadAsync<string>(_ => Task.FromResult<string?>(null));
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Value);
    }

    [Fact]
    public async Task StorageFailureRollsBackBeforeReleasingBoundary()
    {
        var coordinator = new AuthenticationCommitCoordinator();
        var generation = coordinator.BeginChange();
        string? stored = null;
        await Assert.ThrowsAsync<IOException>(() => coordinator.TryCommitAsync(generation,
            _ => { stored = "partially persisted"; return Task.FromException(new IOException("storage failed")); },
            () => Assert.Fail("Failed storage must not publish"),
            () => { stored = null; return Task.CompletedTask; }));
        Assert.Null(stored);
        var next = coordinator.BeginChange();
        Assert.True(await coordinator.TryCommitAsync(next, _ => Task.CompletedTask, () => { }, () => Task.CompletedTask));
    }
}
