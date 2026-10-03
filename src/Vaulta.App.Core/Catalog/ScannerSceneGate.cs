namespace Vaulta.App.Core.Catalog;
/// <summary>Rearms physical occurrences; semantic/quality checks belong to the frame analyzer.</summary>
public sealed class ScannerSceneGate
{
    private byte[]? _consumed;
    private byte[]? _replacement;
    private long _replacementSince;
    private long? _absentSince;
    private bool _removed;
    public bool Observe(byte[] signature, long timeMs, bool cardPresent = true)
    {
        if(signature.Length==0) return false;
        if(!cardPresent)
        {
            _absentSince ??= timeMs;
            if(timeMs-_absentSince.Value>=250) _removed=true;
            _replacement=null; return false;
        }
        _absentSince=null;
        if(_consumed is null || _removed) return true;
        if(Distance(signature,_consumed)<22) { _replacement=null; return false; }
        if(_replacement is null || Distance(signature,_replacement)>15)
        { _replacement=signature.ToArray(); _replacementSince=timeMs; return false; }
        return timeMs-_replacementSince>=100;
    }
    public void Consume(byte[] signature)
    { _consumed=signature.ToArray(); _replacement=null; _absentSince=null; _removed=false; }
    public static bool IsSameScene(byte[] left,byte[] right)=>Distance(left,right)<22;
    private static double Distance(byte[] left,byte[] right)
    {
        if(left.Length!=right.Length) return 255;
        long total=0; for(var i=0;i<left.Length;i++) total+=Math.Abs(left[i]-right[i]);
        return (double)total/left.Length;
    }
}
