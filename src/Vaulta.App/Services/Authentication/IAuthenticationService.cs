using Vaulta.Identity.Contracts;

namespace Vaulta.App.Services.Authentication;

public interface IAuthenticationService
{
    Task<UserSummaryDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<bool> RestoreSessionAsync(CancellationToken cancellationToken = default);
    Task<bool> RefreshAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
}
