using System.Text.Json;

namespace Vaulta.App.Core.Catalog;

public sealed record SessionVideoSnapshot(long PositionUs, decimal TotalValueBrl, decimal? CostBrl, int CardCount, int UnpricedCards,
    string? CardName, decimal? CardValueBrl, bool Highlight, bool PlaySound);
public enum SessionVideoState { Recording, ReadyToExport, Exported, Interrupted }
public sealed record SessionVideoClip(Guid Id, Guid OwnerId, Guid SessionId, DateTimeOffset CreatedAt, SessionVideoState State,
    bool AnimateReveals, IReadOnlyList<SessionVideoSnapshot> Snapshots, string? CaptureCacheFileName = null, long? CaptureDurationUs = null)
{
    public SessionVideoClip AlignToMediaDuration(long mediaDurationUs)
    {
        if (mediaDurationUs <= 0) throw new ArgumentOutOfRangeException(nameof(mediaDurationUs));
        // CameraX starts its encoder asynchronously after StartVideoRecording returns.
        // Its completed media duration excludes that startup delay. Shift the shared
        // sound/overlay timeline together so late reveals remain inside the video.
        if (CaptureDurationUs is not { } elapsed || elapsed <= mediaDurationUs) return this;
        var startupUs = elapsed - mediaDurationUs;
        return this with { Snapshots = Snapshots.Select(x => x with { PositionUs = Math.Max(0, x.PositionUs - startupUs) }).ToArray() };
    }

    public SessionVideoClip AddSnapshot(SessionVideoSnapshot snapshot)
    {
        if (State != SessionVideoState.Recording || snapshot.PositionUs < 0 || Snapshots.Count > 0 && snapshot.PositionUs < Snapshots[^1].PositionUs)
            throw new InvalidOperationException("A linha do tempo da gravação precisa seguir a ordem dos acontecimentos.");
        return this with { Snapshots = Snapshots.Append(snapshot).ToArray() };
    }
    public static SessionVideoSnapshot Snapshot(ScannerSession session, long positionUs, ScannerSessionCard? card = null, bool playSound = false) =>
        new(positionUs, session.EstimatedValueBrl, session.CostBrl, session.Cards.Count, session.UnpricedCards, card?.Name,
            card?.MarketValue?.AmountBrl, card is not null && session.IsHighlight(card), playSound);
}

public interface ISessionVideoExporter
{
    Task Export(SessionVideoClip clip, string rawPath, string outputPath, IProgress<int>? progress, CancellationToken ct);
}

/// <summary>Media is local and scoped to the account/session. Paths are derived from IDs, never stored user paths.</summary>
public sealed class SessionVideoStore(string root, string? captureCacheRoot = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string DirectoryFor(Guid owner, Guid session)
    {
        if (owner == Guid.Empty || session == Guid.Empty) throw new ArgumentException("Conta e sessão são obrigatórias.");
        return Path.Combine(root, owner.ToString("N"), session.ToString("N"));
    }
    public string RawPath(SessionVideoClip clip) => Path.Combine(DirectoryFor(clip.OwnerId, clip.SessionId), clip.Id.ToString("N") + ".raw.mp4");
    public string OutputPath(SessionVideoClip clip) => Path.Combine(DirectoryFor(clip.OwnerId, clip.SessionId), clip.Id.ToString("N") + ".share.mp4");
    public async Task Save(SessionVideoClip clip, CancellationToken ct = default)
    {
        if (clip.Id == Guid.Empty || clip.Snapshots.Count == 0 || clip.Snapshots[0].PositionUs != 0) throw new ArgumentException("Gravação sem linha do tempo inicial.");
        await _gate.WaitAsync(ct); string? temporary = null;
        try
        {
            var directory = DirectoryFor(clip.OwnerId, clip.SessionId); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, clip.Id.ToString("N") + ".json"); temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, clip, cancellationToken: ct); await stream.FlushAsync(ct);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            finally { _gate.Release(); }
        }
    }
    public async Task<IReadOnlyList<SessionVideoClip>> Get(Guid owner, Guid session, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var directory = DirectoryFor(owner, session); if (!Directory.Exists(directory)) return [];
            var clips = new List<SessionVideoClip>();
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                await using var stream = File.OpenRead(file);
                var clip = await JsonSerializer.DeserializeAsync<SessionVideoClip>(stream, cancellationToken: ct);
                if (clip is null || clip.OwnerId != owner || clip.SessionId != session || Path.GetFileName(file) != clip.Id.ToString("N") + ".json")
                    throw new InvalidDataException("Gravação inválida ou de outra conta.");
                if (clip.State is SessionVideoState.Recording or SessionVideoState.Interrupted)
                {
                    if (captureCacheRoot is not null && IsCaptureFileName(clip.CaptureCacheFileName))
                    {
                        var capture = Path.Combine(captureCacheRoot, clip.CaptureCacheFileName!);
                        if (File.Exists(capture) && new FileInfo(capture).Length > 0)
                        {
                            var recoveryPath = RawPath(clip) + ".recovering";
                            try
                            {
                                await using (var raw = new FileStream(capture, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                await using (var recovered = new FileStream(recoveryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                {
                                    await raw.CopyToAsync(recovered, ct); await recovered.FlushAsync(ct);
                                }
                                File.Move(recoveryPath, RawPath(clip), overwrite: true);
                            }
                            catch (IOException) { /* Preserve the source and allow another recovery attempt. */ }
                            finally
                            {
                                try { if (File.Exists(recoveryPath)) File.Delete(recoveryPath); }
                                catch (IOException) { /* A later attempt can overwrite the temporary file. */ }
                            }
                        }
                    }
                    clip = clip with { State = File.Exists(RawPath(clip)) && new FileInfo(RawPath(clip)).Length > 0 ? SessionVideoState.ReadyToExport : SessionVideoState.Interrupted };
                }
                clips.Add(clip);
            }
            return clips.OrderBy(x => x.CreatedAt).ToArray();
        }
        finally { _gate.Release(); }
    }
    public static bool IsCaptureFileName(string? name) => name is not null && name.EndsWith(".mp4", StringComparison.Ordinal)
        && name.Length is > 4 and <= 34 && name.AsSpan(0, name.Length - 4).ToArray().All(c => c is >= '0' and <= '9');
}

/// <summary>Streams a mono WAV; no complete session audio is kept in memory.</summary>
public static class SessionVideoSoundtrack
{
    public const int SampleRate = 48000;
    public static async Task Write(Stream output, IReadOnlyList<SessionVideoSnapshot> snapshots, long durationUs, CancellationToken ct)
    {
        if (durationUs is <= 0 or > (30L * 60 + 5) * 1000000) throw new ArgumentOutOfRangeException(nameof(durationUs), "Grave clipes de até 30 minutos.");
        var samples = checked(durationUs * SampleRate / 1000000); var byteCount = checked((int)(samples * 2));
        using (var header = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            header.Write("RIFF"u8); header.Write(byteCount + 36); header.Write("WAVEfmt "u8); header.Write(16); header.Write((short)1);
            header.Write((short)1); header.Write(SampleRate); header.Write(SampleRate * 2); header.Write((short)2); header.Write((short)16);
            header.Write("data"u8); header.Write(byteCount);
        }
        var effects = snapshots.Where(x => x.PlaySound && x.PositionUs >= 0 && x.PositionUs < durationUs)
            .Select(x => (Start: x.PositionUs * SampleRate / 1000000, Highlight: x.Highlight)).OrderBy(x => x.Start).ToArray();
        var buffer = new byte[8192];
        for (long first = 0; first < samples; first += buffer.Length / 2)
        {
            ct.ThrowIfCancellationRequested(); var count = (int)Math.Min(buffer.Length / 2, samples - first); Array.Clear(buffer);
            foreach (var effect in effects)
            {
                var length = (int)(SampleRate * (effect.Highlight ? 0.65 : 0.25));
                var from = (int)Math.Max(0, effect.Start - first); var to = (int)Math.Min(count, effect.Start + length - first);
                for (var index = from; index < to; index++)
                {
                    var seconds = (first + index - effect.Start) / (double)SampleRate;
                    var frequency = effect.Highlight && seconds > 0.16 ? 1567.98 : 1046.5;
                    var envelope = Math.Min(1, seconds / 0.004) * Math.Exp(-seconds * (effect.Highlight ? 6 : 18));
                    var wave = (Math.Sin(2 * Math.PI * frequency * seconds) + 0.22 * Math.Sin(2 * Math.PI * frequency * 2 * seconds)) * envelope * 5500;
                    var old = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(index * 2, 2));
                    var sample = (short)Math.Clamp(old + (int)wave, short.MinValue, short.MaxValue);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(index * 2, 2), sample);
                }
            }
            await output.WriteAsync(buffer.AsMemory(0, count * 2), ct);
        }
    }
}
