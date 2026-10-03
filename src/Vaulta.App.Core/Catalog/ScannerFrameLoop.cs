namespace Vaulta.App.Core.Catalog;
public enum ScannerFrameKind { Unknown, Front, Back, NoCard }
public sealed class ScannerFrameLoop
{
    private readonly ScannerSceneGate _gate=new();
    private bool _inFlight;
    public bool TryBegin(byte[] signature,long timeMs,ScannerFrameKind kind,bool usable,bool blocked=false)
    {
        var ready=_gate.Observe(signature,timeMs,kind!=ScannerFrameKind.NoCard);
        if(_inFlight || blocked || !usable || kind is ScannerFrameKind.Back or ScannerFrameKind.NoCard || !ready) return false;
        _gate.Consume(signature); _inFlight=true; return true;
    }
    public void Consume(byte[] signature)=>_gate.Consume(signature);
    public void Complete()=>_inFlight=false;
}
