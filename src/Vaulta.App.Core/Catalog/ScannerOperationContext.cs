namespace Vaulta.App.Core.Catalog;

public sealed record ScannerOperationContext(Guid SessionId, Guid OwnerId, long Generation, CancellationToken Token)
{
    public void EnsureCurrent(ScannerSession? session, Guid? owner, long generation, bool visible)
    {
        Token.ThrowIfCancellationRequested();
        if (!visible || generation != Generation || session?.Id != SessionId || session.OwnerId != OwnerId || owner != OwnerId)
            throw new OperationCanceledException("A sessão do scanner foi substituída.");
    }
}
