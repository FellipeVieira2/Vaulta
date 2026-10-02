using System.Net.Http.Json;
using Vaulta.App.Core.Http;
using Vaulta.Marketplace.Contracts;

namespace Vaulta.App.Core.Marketplace;

public interface IListingDraftClient
{
    Task<ListingDraftDto> CreateAsync(CreateListingDraftRequest request, CancellationToken ct = default);
    Task<ListingDraftDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ListingDraftDto> UpdateAsync(Guid id, UpdateListingDraftRequest request, CancellationToken ct = default);
    Task<ListingDraftDto> AddPhotoAsync(Guid id, ListingDraftPhotoRequest request, CancellationToken ct = default);
    Task<ListingDraftDto> RemovePhotoAsync(Guid id, Guid assetId, Guid version, CancellationToken ct = default);
    Task<ListingDraftDto> PublishAsync(Guid id, PublishListingDraftRequest request, CancellationToken ct = default);
    Task<ListingDraftDto> CancelAsync(Guid id, Guid version, CancellationToken ct = default);
}

public sealed class ListingDraftClient(HttpClient http) : IListingDraftClient
{
    private const string Route = "api/v1/me/seller/listing-drafts";
    public Task<ListingDraftDto> CreateAsync(CreateListingDraftRequest request, CancellationToken ct = default) => Send(HttpMethod.Post, Route, request, ct);
    public Task<ListingDraftDto> GetAsync(Guid id, CancellationToken ct = default) => Send(HttpMethod.Get, $"{Route}/{id}", null, ct);
    public Task<ListingDraftDto> UpdateAsync(Guid id, UpdateListingDraftRequest request, CancellationToken ct = default) => Send(HttpMethod.Put, $"{Route}/{id}", request, ct);
    public Task<ListingDraftDto> AddPhotoAsync(Guid id, ListingDraftPhotoRequest request, CancellationToken ct = default) => Send(HttpMethod.Post, $"{Route}/{id}/photos", request, ct);
    public Task<ListingDraftDto> RemovePhotoAsync(Guid id, Guid assetId, Guid version, CancellationToken ct = default) => Send(HttpMethod.Delete, $"{Route}/{id}/photos/{assetId}?version={version}", null, ct);
    public Task<ListingDraftDto> PublishAsync(Guid id, PublishListingDraftRequest request, CancellationToken ct = default) => Send(HttpMethod.Post, $"{Route}/{id}/publish", request, ct);
    public Task<ListingDraftDto> CancelAsync(Guid id, Guid version, CancellationToken ct = default) => Send(HttpMethod.Delete, $"{Route}/{id}?version={version}", null, ct);

    private async Task<ListingDraftDto> Send(HttpMethod method, string uri, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body, body.GetType()) };
        using var response = await http.SendAsync(request, ct);
        return await response.ReadApiJsonAsync<ListingDraftDto>(ct);
    }
}
