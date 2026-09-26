namespace Vaulta.App.Services.Authentication;

public interface ITokenStore
{
    Task<StoredTokens?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed record StoredTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
