using System.Buffers.Binary;
using Vaulta.App.Core.Catalog;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class SessionVideoTests
{
    [Fact]
    public async Task EncoderStartupDelayKeepsLateRevealAndItsSoundInsideTheMovie()
    {
        var clip = new SessionVideoClip(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            SessionVideoState.ReadyToExport, true,
            [new(0, 0, 100, 0, 0, null, null, false, false),
             new(6_000_000, 25, 100, 1, 0, "First", 25, false, true),
             new(8_000_000, 275, 100, 2, 0, "Last", 250, true, true)], CaptureDurationUs: 10_400_000);
        var aligned = clip.AlignToMediaDuration(8_000_000);
        Assert.Equal(new long[] { 0, 3_600_000, 5_600_000 }, aligned.Snapshots.Select(x => x.PositionUs));
        Assert.Equal(275m, aligned.Snapshots[^1].TotalValueBrl);
        Assert.Equal(8_000_000, clip.Snapshots[^1].PositionUs);
        using var output = new MemoryStream();
        await SessionVideoSoundtrack.Write(output, aligned.Snapshots, 8_000_000, CancellationToken.None);
        var samples = output.ToArray().Skip(44).ToArray();
        Assert.Contains(samples.Skip(5_600 * SessionVideoSoundtrack.SampleRate / 1_000 * 2).Take(24000), x => x != 0);
        Assert.All(samples.TakeLast(2000), x => Assert.Equal(0, x));
        Assert.Same(clip, clip.AlignToMediaDuration(11_000_000));
        var legacy = clip with { CaptureDurationUs = null };
        Assert.Same(legacy, legacy.AlignToMediaDuration(8_000_000));
    }

    [Fact]
    public async Task LockedCameraFileDoesNotCreatePartialRawMediaAndRecoveryCanBeRetried()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vaulta-video-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(directory, "cache"); Directory.CreateDirectory(cache);
        try
        {
            var owner = Guid.NewGuid(); var session = ScannerSession.Start(owner, DateTimeOffset.UtcNow);
            var clip = new SessionVideoClip(Guid.NewGuid(), owner, session.Id, DateTimeOffset.UtcNow, SessionVideoState.Interrupted,
                true, [SessionVideoClip.Snapshot(session, 0)], "456789.mp4");
            var store = new SessionVideoStore(Path.Combine(directory, "videos"), cache); await store.Save(clip);
            var source = Path.Combine(cache, "456789.mp4"); await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
            using (var locked = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Equal(SessionVideoState.Interrupted, Assert.Single(await store.Get(owner, session.Id)).State);
                Assert.False(File.Exists(store.RawPath(clip)));
            }
            // A failed camera copy can also leave a nonempty, incomplete raw file.
            await File.WriteAllBytesAsync(store.RawPath(clip), [7]);
            await File.WriteAllBytesAsync(store.RawPath(clip) + ".recovering", [9, 9]);
            Assert.Equal(SessionVideoState.ReadyToExport, Assert.Single(await store.Get(owner, session.Id)).State);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(store.RawPath(clip)));
            Assert.False(File.Exists(store.RawPath(clip) + ".recovering")); Assert.True(File.Exists(source));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ExportSoundtrackPlacesEffectsAtMediaTimeAndKeepsSilenceOutsideThem()
    {
        using var output = new MemoryStream();
        var effect = new SessionVideoSnapshot(1_000_000, 150, 100, 1, 0, "Pikachu", 150, true, true);
        await SessionVideoSoundtrack.Write(output, [effect], 2_000_000, CancellationToken.None);
        var bytes = output.ToArray();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(SessionVideoSoundtrack.SampleRate, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4)));
        Assert.Equal(2 * SessionVideoSoundtrack.SampleRate * 2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4)));
        Assert.All(bytes.Skip(44).Take(SessionVideoSoundtrack.SampleRate * 2), sample => Assert.Equal(0, sample));
        Assert.Contains(bytes.Skip(44 + SessionVideoSoundtrack.SampleRate * 2).Take(24000), sample => sample != 0);
        Assert.All(bytes.TakeLast(2000), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public async Task MutedRevealDoesNotInsertSoundAndCancelledExportStopsWriting()
    {
        using var muted = new MemoryStream();
        await SessionVideoSoundtrack.Write(muted, [new(0, 150, null, 1, 0, "Card", 150, true, false)], 100_000, CancellationToken.None);
        Assert.All(muted.ToArray().Skip(44), sample => Assert.Equal(0, sample));
        using var cancelled = new MemoryStream(); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SessionVideoSoundtrack.Write(cancelled, [], 1_000_000, cts.Token));
    }

    [Fact]
    public async Task ClipRestoresRawMediaAfterInterruptedRecordingAndRemainsScopedToItsOwner()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vaulta-video-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(directory, "cache"); Directory.CreateDirectory(cache);
        try
        {
            var owner = Guid.NewGuid(); var session = ScannerSession.Start(owner, DateTimeOffset.UtcNow);
            var clip = new SessionVideoClip(Guid.NewGuid(), owner, session.Id, DateTimeOffset.UtcNow, SessionVideoState.Recording,
                true, [SessionVideoClip.Snapshot(session, 0)], "123456789.mp4");
            var store = new SessionVideoStore(Path.Combine(directory, "videos"), cache); await store.Save(clip);
            await File.WriteAllBytesAsync(Path.Combine(cache, "123456789.mp4"), [1, 2, 3, 4]);
            var recovered = Assert.Single(await store.Get(owner, session.Id)); Assert.Equal(SessionVideoState.ReadyToExport, recovered.State);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(store.RawPath(recovered)));
            Assert.Empty(await store.Get(Guid.NewGuid(), session.Id));
            Assert.Throws<InvalidOperationException>(() => recovered.AddSnapshot(new(1, 0, null, 0, 0, null, null, false, false)));
            var later = clip.AddSnapshot(new(2_000_000, 30, null, 1, 0, "Card", 30, false, true));
            Assert.Throws<InvalidOperationException>(() => later.AddSnapshot(new(1_000_000, 30, null, 1, 0, "Card", 30, false, true)));
            Assert.False(SessionVideoStore.IsCaptureFileName("../123.mp4")); Assert.False(SessionVideoStore.IsCaptureFileName("/123.mp4"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
