namespace Vaulta.App.Core.Identity;

public sealed record AuthenticationSessionSnapshot<T>(long Generation, T? Value) where T : class;

/// <summary>Serializes uncancellable token storage and rejects superseded session mutations.</summary>
public sealed class AuthenticationCommitCoordinator
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _generation;
    private bool _changeInProgress;

    public long BeginChange(Action? invalidateSession = null)
    {
        lock (_lock)
        {
            _generation++;
            _changeInProgress = true;
        }
        invalidateSession?.Invoke();
        return _generation;
    }

    public async Task<bool> TryCommitAsync(long generation, Func<CancellationToken, Task> persist, Action publish,
        Func<Task> invalidate, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(generation))
                return false;

            try
            {
                await persist(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await SafeInvalidate(invalidate).ConfigureAwait(false);
                ReleaseIfCurrent(generation);
                return false;
            }
            catch
            {
                await SafeInvalidate(invalidate).ConfigureAwait(false);
                ReleaseIfCurrent(generation);
                throw;
            }

            if (!IsCurrent(generation) || cancellationToken.IsCancellationRequested)
            {
                await SafeInvalidate(invalidate).ConfigureAwait(false);
                ReleaseIfCurrent(generation);
                return false;
            }

            lock (_lock)
            {
                if (_generation != generation)
                    return false;
                publish();
                _changeInProgress = false;
            }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> InvalidateAsync(long generation, Func<Task> invalidate)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(generation))
                return false;

            await SafeInvalidate(invalidate).ConfigureAwait(false);

            lock (_lock)
            {
                if (_generation != generation)
                    return false;
                _changeInProgress = false;
            }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AuthenticationSessionSnapshot<T>?> ReadAsync<T>(Func<CancellationToken, Task<T?>> read,
        CancellationToken cancellationToken = default) where T : class
    {
        long capturedGeneration;
        lock (_lock)
        {
            if (_changeInProgress)
                return null;
            capturedGeneration = _generation;
        }

        var value = await read(cancellationToken).ConfigureAwait(false);
        return new AuthenticationSessionSnapshot<T>(capturedGeneration, value);
    }

    public Task<bool> TryPublishAsync(long generation, Action publish, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_generation != generation)
                return Task.FromResult(false);
            publish();
            return Task.FromResult(true);
        }
    }

    private bool IsCurrent(long generation)
    {
        lock (_lock)
            return _generation == generation;
    }

    private void ReleaseIfCurrent(long generation)
    {
        lock (_lock)
        {
            if (_generation == generation)
                _changeInProgress = false;
        }
    }

    private static async Task SafeInvalidate(Func<Task> invalidate)
    {
        try
        {
            await invalidate().ConfigureAwait(false);
        }
        catch
        {
            // Swallow: invalidate is best-effort cleanup; original exception/cancellation takes precedence.
        }
    }
}