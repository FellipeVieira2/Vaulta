using Vaulta.App.Core.Http;
using Vaulta.App.State;

namespace Vaulta.App.Services.Authentication;

/// <summary>
/// Adapts the MAUI app's <see cref="ITokenStore"/>/<see cref="IAuthenticationService"/> to the
/// MAUI-agnostic <see cref="IAccessTokenProvider"/> seam used by
/// <see cref="Vaulta.App.Core.Http.AuthorizingHttpMessageHandler"/>. Keeps Vaulta.App.Core free
/// of any SecureStorage/Shell dependency.
/// </summary>
public sealed class AppAccessTokenProvider(ITokenStore tokenStore, IAuthenticationService authenticationService, SessionState sessionState)
    : IAccessTokenProvider
{
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var tokens = await tokenStore.GetAsync(cancellationToken);
        return tokens?.AccessToken;
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await authenticationService.ForceRefreshAsync(cancellationToken);
        }
        catch (Exception)
        {
            // A failed refresh (e.g. the refresh token was revoked/expired) is not a transport
            // fault to retry - it means the session cannot be recovered.
            return false;
        }
    }

    public async Task NotifySessionExpiredAsync()
    {
        await tokenStore.ClearAsync(CancellationToken.None);
        sessionState.User = null;
        if (Shell.Current is AppShell appShell)
            await MainThread.InvokeOnMainThreadAsync(() => appShell.ShowLoginAsync());
    }
}
