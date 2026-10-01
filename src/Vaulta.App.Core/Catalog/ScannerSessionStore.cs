using System.Text.Json;

namespace Vaulta.App.Core.Catalog;

public interface IScannerSessionStore
{
    Task<ScannerSession?> Latest(Guid owner, CancellationToken ct = default);
    Task Save(ScannerSession session, CancellationToken ct = default);
}

public sealed class FileScannerSessionStore(string root) : IScannerSessionStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<ScannerSession?> Latest(Guid owner, CancellationToken ct = default)
    {
        if (owner == Guid.Empty) throw new ArgumentException("Owner required.");
        await _gate.WaitAsync(ct);
        try
        {
            var directory = Path.Combine(root, owner.ToString("N"));
            if (!Directory.Exists(directory)) return null;
            var file = new DirectoryInfo(directory).GetFiles("*.json").OrderByDescending(x => x.LastWriteTimeUtc).ThenByDescending(x => x.Name).FirstOrDefault();
            if (file is null) return null;
            await using var stream = File.OpenRead(file.FullName);
            var session = await JsonSerializer.DeserializeAsync<ScannerSession>(stream, cancellationToken: ct);
            if (session is null || session.OwnerId != owner || file.Name != session.Id.ToString("N") + ".json")
                throw new InvalidDataException("A sessão salva não corresponde à sua conta.");
            return session;
        }
        finally { _gate.Release(); }
    }
    public async Task Save(ScannerSession session, CancellationToken ct = default)
    {
        if (session.OwnerId == Guid.Empty || session.Id == Guid.Empty) throw new ArgumentException("Session and owner required.");
        await _gate.WaitAsync(ct);
        var temporary = string.Empty;
        try
        {
            var directory = Path.Combine(root, session.OwnerId.ToString("N")); Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, session.Id.ToString("N") + ".json"); temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, session, cancellationToken: ct);
                await stream.FlushAsync(ct);
            }
            File.Move(temporary, file, true);
        }
        finally
        {
            try { if (temporary.Length > 0 && File.Exists(temporary)) File.Delete(temporary); }
            finally { _gate.Release(); }
        }
    }
}
