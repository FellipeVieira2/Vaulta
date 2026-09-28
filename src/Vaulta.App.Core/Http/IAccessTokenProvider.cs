namespace Vaulta.App.Core.Http;

/// <summary>
/// MAUI-agnostic seam the <see cref="AuthorizingHttpMessageHandler"/> uses to read the current
/// access token, force a refresh, and react when the session can no longer be recovered.
/// Implemented in the app host over <c>ITokenStore</c>/<c>IAuthenticationService</c> so this
/// library never depends on SecureStorage or Shell navigation directly.
/// </summary>
public interface IAccessTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>Forces a network refresh (ignores any "still valid" short-circuit) and returns whether it succeeded.</summary>
    Task<bool> RefreshAsync(CancellationToken cancellationToken);

    /// <summary>Called once, after a refresh attempt fails, so the host can clear the session and return to login.</summary>
    Task NotifySessionExpiredAsync();
}
