using Vaulta.App.Core.Identity;
using Vaulta.App.Services.Api;
using Vaulta.App.State;
using Vaulta.Identity.Contracts;

namespace Vaulta.App.Services.Authentication;

public sealed class AuthenticationService(
    IVaultaApiClient apiClient,
    ITokenStore tokenStore,
    SessionState sessionState,
    TimeProvider timeProvider,
    AuthenticationCommitCoordinator coordinator) : IAuthenticationService
{
    private static readonly TimeSpan RefreshSafetyWindow = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public async Task<UserSummaryDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        await apiClient.RegisterAsync(request, cancellationToken);

    public async Task LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var response = await apiClient.LoginAsync(request, cancellationToken);
        var storedTokens = ToStoredTokens(response);
        var user = response.User;
        var generation = coordinator.BeginChange();
        await coordinator.TryCommitAsync(
            generation,
            ct => tokenStore.SaveAsync(storedTokens, ct),
            () => sessionState.User = user,
            ClearTokensAsync,
            cancellationToken);
    }

    public async Task<bool> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await coordinator.ReadAsync<StoredTokens>(tokenStore.GetAsync, cancellationToken);
        if (snapshot is null || snapshot.Value is null)
            return false;

        var tokens = snapshot.Value;
        if (tokens.ExpiresAt <= timeProvider.GetUtcNow())
        {
            if (!await RefreshAsync(cancellationToken))
                return false;
            // Re-read after refresh to get the updated tokens under a valid generation.
            snapshot = await coordinator.ReadAsync<StoredTokens>(tokenStore.GetAsync, cancellationToken);
            if (snapshot is null || snapshot.Value is null)
                return false;
            tokens = snapshot.Value;
        }

        var profile = await apiClient.GetCurrentUserAsync(tokens.AccessToken, cancellationToken);
        var user = new UserSummaryDto(profile.Id, profile.Username, profile.DisplayName, profile.AvatarUrl);
        if (!await coordinator.TryPublishAsync(snapshot.Generation, () => sessionState.User = user, cancellationToken))
            return false;
        return true;
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await coordinator.ReadAsync<StoredTokens>(tokenStore.GetAsync, cancellationToken);
            if (snapshot is null || snapshot.Value is null || string.IsNullOrWhiteSpace(snapshot.Value.RefreshToken))
                return false;

            if (snapshot.Value.ExpiresAt > timeProvider.GetUtcNow() + RefreshSafetyWindow)
                return true;

            return await RefreshCoreAsync(snapshot.Value, snapshot.Generation, cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<bool> ForceRefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await coordinator.ReadAsync<StoredTokens>(tokenStore.GetAsync, cancellationToken);
            if (snapshot is null || snapshot.Value is null || string.IsNullOrWhiteSpace(snapshot.Value.RefreshToken))
                return false;

            // Bypasses the "still valid" short-circuit: a real 401 from the server means the
            // client's own expiry estimate can no longer be trusted, so a network refresh is forced.
            return await RefreshCoreAsync(snapshot.Value, snapshot.Generation, cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<bool> RefreshCoreAsync(StoredTokens tokens, long generation, CancellationToken cancellationToken)
    {
        var response = await apiClient.RefreshAsync(new RefreshRequest(tokens.RefreshToken), cancellationToken);
        var storedTokens = ToStoredTokens(response);
        var user = response.User;
        return await coordinator.TryCommitAsync(
            generation,
            ct => tokenStore.SaveAsync(storedTokens, ct),
            () => sessionState.User = user,
            ClearTokensAsync,
            cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var generation = coordinator.BeginChange(() => sessionState.User = null);
        await coordinator.InvalidateAsync(generation, async () =>
        {
            try
            {
                var tokens = await tokenStore.GetAsync(CancellationToken.None);
                if (tokens is not null && !string.IsNullOrWhiteSpace(tokens.AccessToken) && !string.IsNullOrWhiteSpace(tokens.RefreshToken))
                    await apiClient.LogoutAsync(new RefreshRequest(tokens.RefreshToken), tokens.AccessToken, CancellationToken.None);
            }
            catch
            {
                // Best-effort server logout; local cleanup must always proceed.
            }
            await tokenStore.ClearAsync(CancellationToken.None);
        });
    }

    private async Task ClearTokensAsync()
    {
        try { await tokenStore.ClearAsync(CancellationToken.None); } catch { /* best-effort */ }
        sessionState.User = null;
    }

    private StoredTokens ToStoredTokens(AuthResponse response) =>
        new(response.AccessToken, response.RefreshToken, timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn));
}