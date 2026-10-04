using System.Threading.Channels;
namespace Vaulta.Vision.Infrastructure;

// A bounded notification, not a queue of scans. Events received during a load remain dirty.
public sealed class VisualIndexRefreshSignal
{
    private readonly Channel<bool> _changes=Channel.CreateBounded<bool>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropWrite,SingleReader=true});
    private long _requested, _completed, _lastRefreshTicks;
    public long RequestedRevision=>Interlocked.Read(ref _requested);
    public bool Pending=>RequestedRevision>Interlocked.Read(ref _completed);
    public DateTimeOffset? LastRefresh=>Interlocked.Read(ref _lastRefreshTicks) is >0 and var ticks ? new(ticks,TimeSpan.Zero):null;
    public void Request(){Interlocked.Increment(ref _requested);_changes.Writer.TryWrite(true);}
    public void Completed(long revision,DateTimeOffset at){Interlocked.Exchange(ref _completed,revision);Interlocked.Exchange(ref _lastRefreshTicks,at.UtcTicks);}
    public async Task<bool> WaitAsync(TimeSpan periodic,TimeSpan debounce,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(periodic);
        try {await _changes.Reader.ReadAsync(timeout.Token);}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return false;}
        await Task.Delay(debounce,ct);
        while(_changes.Reader.TryRead(out _)) { }
        return true;
    }
}
