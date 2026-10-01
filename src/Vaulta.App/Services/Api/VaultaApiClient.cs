using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vaulta.Identity.Contracts;

namespace Vaulta.App.Services.Api;

public sealed class VaultaApiClient(HttpClient httpClient) : IVaultaApiClient
{
    public Task<UserSummaryDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<RegisterRequest, UserSummaryDto>("api/v1/auth/register", request, cancellationToken);

    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<LoginRequest, AuthResponse>("api/v1/auth/login", request, cancellationToken);

    public Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<RefreshRequest, AuthResponse>("api/v1/auth/refresh", request, cancellationToken);

    public async Task LogoutAsync(RefreshRequest request, string accessToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/logout")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<MyProfileDto> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "api/v1/me");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MyProfileDto>(cancellationToken)
            ?? throw new InvalidDataException("The API returned an empty profile response.");
    }

    public async Task UpdateShippingAddressAsync(string accessToken, string? street, string? number, string? complement, string? neighborhood, string? city, string? state, string? zipCode, string? recipient, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, "api/v1/me/shipping-address");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Content = JsonContent.Create(new { Street = street, Number = number, Complement = complement, Neighborhood = neighborhood, City = city, State = state, ZipCode = zipCode, Recipient = recipient });
        using var response = await httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(path, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new InvalidDataException("The API returned an empty response.");
    }
}
