using System.Text.Json;

namespace Vaulta.App.Services.Authentication;

public sealed class SecureTokenStore : ITokenStore
{
    private const string StorageKey = "vaulta.authentication.tokens";

    public async Task<StoredTokens?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await SecureStorage.Default.GetAsync(StorageKey);
        if (value is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<StoredTokens>(value);
        }
        catch (JsonException)
        {
            SecureStorage.Default.Remove(StorageKey);
            return null;
        }
    }

    public Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(tokens));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(StorageKey);
        return Task.CompletedTask;
    }
}
