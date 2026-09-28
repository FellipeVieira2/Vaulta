using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vaulta.App.Core.Http;
using Vaulta.Assets.Contracts;

namespace Vaulta.App.Core.Assets;

public interface IAssetClient
{
    Task<CreateAssetUploadResponse> CreateUploadAsync(CreateAssetUploadRequest request, CancellationToken cancellationToken = default);
    Task<ConfirmAssetUploadResponse> ConfirmUploadAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task UploadToPresignedUrlAsync(string uploadUrl, Stream content, string contentType, CancellationToken cancellationToken = default);
}

/// <summary>
/// HTTP client for the Assets module's presigned-upload flow: create upload -> PUT bytes
/// directly to S3/MinIO using the presigned URL -> confirm. The presigned PUT intentionally
/// uses a bare <see cref="HttpClient"/> (via <paramref name="presignedUploadClient"/>) because
/// the URL's own signature must be the only authorization on that request.
/// </summary>
public sealed class AssetClient(HttpClient apiClient, HttpClient presignedUploadClient) : IAssetClient
{
    public async Task<CreateAssetUploadResponse> CreateUploadAsync(CreateAssetUploadRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.PostAsJsonAsync("api/v1/assets/uploads", request, cancellationToken);
        return await response.ReadApiJsonAsync<CreateAssetUploadResponse>(cancellationToken);
    }

    public async Task<ConfirmAssetUploadResponse> ConfirmUploadAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.PostAsync($"api/v1/assets/{assetId}/confirm", content: null, cancellationToken);
        return await response.ReadApiJsonAsync<ConfirmAssetUploadResponse>(cancellationToken);
    }

    public async Task UploadToPresignedUrlAsync(string uploadUrl, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new StreamContent(content) };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await presignedUploadClient.SendAsync(message, cancellationToken);
        await response.EnsureApiSuccessAsync(cancellationToken);
    }
}
