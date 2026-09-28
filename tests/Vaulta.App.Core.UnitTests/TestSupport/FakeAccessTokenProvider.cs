using Vaulta.App.Core.Http;

namespace Vaulta.App.Core.UnitTests.TestSupport;

public sealed class FakeAccessTokenProvider : IAccessTokenProvider
{
    private int _refreshCalls;
    private int _sessionExpiredCalls;

    public string? Token { get; set; } = "token-v1";
    public bool RefreshSucceeds { get; set; } = true;
    public string? TokenAfterRefresh { get; set; } = "token-v2";
    public TaskCompletionSource? RefreshGate { get; set; }
    public int RefreshCalls => _refreshCalls;
    public int SessionExpiredCalls => _sessionExpiredCalls;

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(Token);

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _refreshCalls);
        if (RefreshGate is not null)
            await RefreshGate.Task;

        if (RefreshSucceeds)
            Token = TokenAfterRefresh;
        return RefreshSucceeds;
    }

    public Task NotifySessionExpiredAsync()
    {
        Interlocked.Increment(ref _sessionExpiredCalls);
        return Task.CompletedTask;
    }
}