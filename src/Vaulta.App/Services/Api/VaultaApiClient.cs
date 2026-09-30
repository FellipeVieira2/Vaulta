using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vaulta.Catalog.Contracts;
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

    public async Task<CardScanResultDto> ScanCardAsync(byte[] imageData, string? gameCode, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(imageData);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(imageContent, "image", "card.jpg");
        if (gameCode is not null)
            content.Add(new StringContent(gameCode), "gameCode");

        using var response = await httpClient.PostAsync("api/v1/scanner/identify", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CardScanResultDto>(cancellationToken)
            ?? throw new InvalidDataException("The scanner API returned an empty response.");
    }

    public async Task<CardScanResultDto> SearchCardsAsync(string query, string? gameCode, CancellationToken cancellationToken = default)
    {
        var url = $"api/v1/scanner/search?query={Uri.EscapeDataString(query)}";
        if (gameCode is not null)
            url += $"&gameCode={Uri.EscapeDataString(gameCode)}";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CardScanResultDto>(cancellationToken)
            ?? throw new InvalidDataException("The search API returned an empty response.");
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(path, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new InvalidDataException("The API returned an empty response.");
    }
}
