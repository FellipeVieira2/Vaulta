using System.Net.Http.Json;
using System.Text.Json;

namespace Vaulta.App.Core.Http;

public static class HttpResponseExtensions
{
    /// <summary>
    /// Throws a translated <see cref="ApiException"/> for any non-success response. The
    /// server's ProblemDetails "title" is captured as internal detail only.
    /// </summary>
    public static async Task EnsureApiSuccessAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        string? detail = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(cancellationToken);
            detail = problem?.Title ?? problem?.Detail;
        }
        catch (JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }

        throw ApiErrorTranslator.FromStatus((int)response.StatusCode, detail);
    }

    public static async Task<T> ReadApiJsonAsync<T>(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await response.EnsureApiSuccessAsync(cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new ApiException(null, "O servidor retornou uma resposta vazia.");
    }
}
