using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
        var resolver = new DefaultJsonTypeInfoResolver();
        if (response.RequestMessage?.RequestUri is { IsAbsoluteUri: true } origin)
            resolver.Modifiers.Add(info =>
            {
                foreach (var property in info.Properties.Where(p => p.Name.Equals("artworkUrl", StringComparison.OrdinalIgnoreCase) && p.PropertyType == typeof(string)))
                    property.CustomConverter = new OwnedArtworkConverter(origin);
            });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
        return await response.Content.ReadFromJsonAsync<T>(options, cancellationToken)
            ?? throw new ApiException(null, "O servidor retornou uma resposta vazia.");
    }
    private sealed class OwnedArtworkConverter(Uri origin) : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            return value?.StartsWith("/api/v1/catalog/printings/", StringComparison.Ordinal) == true
                ? new Uri(origin, value).AbsoluteUri : value;
        }
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}
