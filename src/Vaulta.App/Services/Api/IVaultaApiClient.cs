using Vaulta.Identity.Contracts;

namespace Vaulta.App.Services.Api;

public interface IVaultaApiClient
{
    Task<UserSummaryDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(RefreshRequest request, string accessToken, CancellationToken cancellationToken = default);
    Task<MyProfileDto> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken = default);
    Task UpdateShippingAddressAsync(string accessToken, string? street, string? number, string? complement, string? neighborhood, string? city, string? state, string? zipCode, string? recipient, CancellationToken cancellationToken = default);
}
