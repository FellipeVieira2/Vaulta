using Vaulta.Identity.Contracts;

namespace Vaulta.App.Services.Authentication;

public interface IAuthenticationService
{
    Task<UserSummaryDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<bool> RestoreSessionAsync(CancellationToken cancellationToken = default);
    Task<bool> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a network refresh even if the stored access token still looks unexpired.
    /// Used by <see cref="Vaulta.App.Core.Http.AuthorizingHttpMessageHandler"/> reacting to a
    /// real 401 from the server, where the client's own expiry estimate can no longer be trusted.
    /// </summary>
    Task<bool> ForceRefreshAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
}
