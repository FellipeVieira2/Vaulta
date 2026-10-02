namespace Vaulta.App.Core.Catalog;

/// <summary>Local preview sampling gates recognition requests, never identifies a card itself.</summary>
public sealed class ScannerSceneGate
{
    private byte[]? _stable;
    private byte[]? _consumed;
    private long _stableSince;
    private bool _changed;

    public bool Observe(byte[] signature, long timeMs, bool cardPresent = true)
    {
        if (signature.Length == 0) return false;
        // Lower change threshold so the gate resets faster when a new card enters.
        if (_consumed is not null && Distance(signature, _consumed) >= 16) _changed = true;
        if (!cardPresent) { _stable = null; return false; }
        // Tighter stability band and shorter dwell: trigger capture sooner
        // so the user can point-and-shoot without holding still for a full second.
        if (_stable is null || Distance(signature, _stable) > 7)
        {
            _stable = signature.ToArray(); _stableSince = timeMs; return false;
        }
        return timeMs - _stableSince >= 550 && (_consumed is null || _changed);
    }

    public void Consume(byte[] signature)
    {
        _consumed = signature.ToArray(); _changed = false;
    }

    public static bool IsSameScene(byte[] left, byte[] right) => Distance(left, right) < 22;

    private static double Distance(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return 255;
        long total = 0;
        for (var i = 0; i < left.Length; i++) total += Math.Abs(left[i] - right[i]);
        return (double)total / left.Length;
    }
}
