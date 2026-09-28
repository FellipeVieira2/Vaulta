using Vaulta.App.Services.Api;
using Vaulta.App.State;
using Vaulta.Identity.Contracts;

namespace Vaulta.App.Services.Authentication;

public sealed class AuthenticationService(
    IVaultaApiClient apiClient,
    ITokenStore tokenStore,
    SessionState sessionState,
    TimeProvider timeProvider) : IAuthenticationService
{
    private static readonly TimeSpan RefreshSafetyWindow = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public async Task<UserSummaryDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        await apiClient.RegisterAsync(request, cancellationToken);

    public async Task LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var response = await apiClient.LoginAsync(request, cancellationToken);
        await tokenStore.SaveAsync(ToStoredTokens(response), cancellationToken);
        sessionState.User = response.User;
    }

    public async Task<bool> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await tokenStore.GetAsync(cancellationToken);
        if (tokens is null)
            return false;

        if (tokens.ExpiresAt <= timeProvider.GetUtcNow() && !await RefreshAsync(cancellationToken))
            return false;

        tokens = await tokenStore.GetAsync(cancellationToken);
        if (tokens is null)
            return false;

        var profile = await apiClient.GetCurrentUserAsync(tokens.AccessToken, cancellationToken);
        sessionState.User = new UserSummaryDto(profile.Id, profile.Username, profile.DisplayName, profile.AvatarUrl);
        return true;
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var tokens = await tokenStore.GetAsync(cancellationToken);
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.RefreshToken))
                return false;

            if (tokens.ExpiresAt > timeProvider.GetUtcNow() + RefreshSafetyWindow)
                return true;

            return await RefreshCoreAsync(tokens, cancellationToken);
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
            var tokens = await tokenStore.GetAsync(cancellationToken);
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.RefreshToken))
                return false;

            // Bypasses the "still valid" short-circuit: a real 401 from the server means the
            // client's own expiry estimate can no longer be trusted, so a network refresh is forced.
            return await RefreshCoreAsync(tokens, cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<bool> RefreshCoreAsync(StoredTokens tokens, CancellationToken cancellationToken)
    {
        var response = await apiClient.RefreshAsync(new RefreshRequest(tokens.RefreshToken), cancellationToken);
        await tokenStore.SaveAsync(ToStoredTokens(response), cancellationToken);
        sessionState.User = response.User;
        return true;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await tokenStore.GetAsync(cancellationToken);
        try
        {
            if (tokens is not null && !string.IsNullOrWhiteSpace(tokens.AccessToken) && !string.IsNullOrWhiteSpace(tokens.RefreshToken))
                await apiClient.LogoutAsync(new RefreshRequest(tokens.RefreshToken), tokens.AccessToken, cancellationToken);
        }
        finally
        {
            try
            {
                await tokenStore.ClearAsync(CancellationToken.None);
            }
            finally
            {
                sessionState.User = null;
            }
        }
    }

    private StoredTokens ToStoredTokens(AuthResponse response) =>
        new(response.AccessToken, response.RefreshToken, timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn));
}
