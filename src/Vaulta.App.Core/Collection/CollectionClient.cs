using System.Net.Http.Json;
using Vaulta.App.Core.Http;
using Vaulta.Collection.Contracts;

namespace Vaulta.App.Core.Collection;

public interface ICollectionClient
{
    Task<CollectionPageDto> GetCollectionAsync(CollectionQuery query, CancellationToken cancellationToken = default);
    Task<CollectionSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<CollectionValuationDto> GetValuationAsync(Guid? entryId = null, CancellationToken cancellationToken = default);
    Task<AddCollectibleItemsResponse> AddItemsAsync(AddCollectibleItemsRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<CollectionEntryDetailsDto> GetEntryAsync(Guid entryId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<CollectibleItemDto> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default);
    Task<Guid> UpdateItemAsync(Guid itemId, UpdateCollectibleItemRequest request, CancellationToken cancellationToken = default);
    Task RemoveItemAsync(Guid itemId, Guid version, CancellationToken cancellationToken = default);
}

/// <summary>
/// HTTP client for the authenticated Collection endpoints. Reuses the backend's own contracts;
/// it does not duplicate condition/version/ownership rules, those stay authoritative server-side.
/// </summary>
public sealed class CollectionClient(HttpClient httpClient) : ICollectionClient
{
    public async Task<CollectionPageDto> GetCollectionAsync(CollectionQuery query, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/me/collection{BuildQueryString(query)}", cancellationToken);
        return await response.ReadApiJsonAsync<CollectionPageDto>(cancellationToken);
    }

    public async Task<CollectionSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/v1/me/collection/summary", cancellationToken);
        return await response.ReadApiJsonAsync<CollectionSummaryDto>(cancellationToken);
    }

    public async Task<CollectionValuationDto> GetValuationAsync(Guid? entryId = null, CancellationToken cancellationToken = default)
    {
        var query = entryId.HasValue ? $"?entryId={entryId.Value:D}" : "";
        using var response = await httpClient.GetAsync("api/v1/me/collection/valuation" + query, cancellationToken);
        return await response.ReadApiJsonAsync<CollectionValuationDto>(cancellationToken);
    }

    public async Task<AddCollectibleItemsResponse> AddItemsAsync(AddCollectibleItemsRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("An Idempotency-Key is required to add items to the collection.", nameof(idempotencyKey));

        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/me/collection/items") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        return await response.ReadApiJsonAsync<AddCollectibleItemsResponse>(cancellationToken);
    }

    public async Task<CollectionEntryDetailsDto> GetEntryAsync(Guid entryId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/me/collection/entries/{entryId}?page={page}&pageSize={pageSize}", cancellationToken);
        return await response.ReadApiJsonAsync<CollectionEntryDetailsDto>(cancellationToken);
    }

    public async Task<CollectibleItemDto> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/me/collection/items/{itemId}", cancellationToken);
        return await response.ReadApiJsonAsync<CollectibleItemDto>(cancellationToken);
    }

    public async Task<Guid> UpdateItemAsync(Guid itemId, UpdateCollectibleItemRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"api/v1/me/collection/items/{itemId}", request, cancellationToken);
        await response.EnsureApiSuccessAsync(cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<VersionResponse>(cancellationToken);
        return payload?.Version ?? request.Version;
    }

    public async Task RemoveItemAsync(Guid itemId, Guid version, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"api/v1/me/collection/items/{itemId}?version={version}", cancellationToken);
        await response.EnsureApiSuccessAsync(cancellationToken);
    }

    private static string BuildQueryString(CollectionQuery query)
    {
        var parameters = new List<string>(8);
        void AddIfPresent(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) parameters.Add($"{key}={Uri.EscapeDataString(value)}");
        }

        AddIfPresent("query", query.Query);
        AddIfPresent("game", query.Game);
        if (query.SetId is { } setId) parameters.Add($"setId={setId}");
        AddIfPresent("condition", query.Condition);
        if (query.VariantId is { } variantId) parameters.Add($"variantId={variantId}");
        parameters.Add($"page={query.Page}");
        parameters.Add($"pageSize={query.PageSize}");
        AddIfPresent("sort", query.Sort);
        return parameters.Count == 0 ? string.Empty : "?" + string.Join('&', parameters);
    }

    private sealed record VersionResponse(Guid Version);
}
