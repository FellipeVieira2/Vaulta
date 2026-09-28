using System.Net;
using System.Net.Http.Headers;

namespace Vaulta.App.Core.Http;

/// <summary>
/// Attaches the current Bearer access token to every request. On a single 401, serializes one
/// refresh across all concurrent callers (they observe the same in-flight refresh instead of
/// each triggering their own) and retries the original request exactly once with the new token.
/// If the refresh fails, notifies the host once and returns the original 401 - it never loops.
/// </summary>
public sealed class AuthorizingHttpMessageHandler(IAccessTokenProvider tokenProvider) : DelegatingHandler
{
    private readonly object _refreshLock = new();
    private Task<bool>? _refreshInFlight;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[]? contentBytes = null;
        MediaTypeHeaderValue? contentType = null;
        if (request.Content is not null)
        {
            contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            contentType = request.Content.Headers.ContentType;
        }

        HttpRequestMessage Build()
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (contentBytes is not null)
            {
                clone.Content = new ByteArrayContent(contentBytes);
                clone.Content.Headers.ContentType = contentType;
            }
            return clone;
        }

        var first = Build();
        await AttachTokenAsync(first, cancellationToken);
        var response = await base.SendAsync(first, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        var refreshed = await RefreshOnceAsync();
        if (!refreshed)
        {
            await tokenProvider.NotifySessionExpiredAsync();
            return response;
        }

        response.Dispose();
        var retry = Build();
        await AttachTokenAsync(retry, cancellationToken);
        return await base.SendAsync(retry, cancellationToken);
    }

    private async Task AttachTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private Task<bool> RefreshOnceAsync()
    {
        lock (_refreshLock)
        {
            // Concurrent 401s reuse this single in-flight refresh instead of each starting their own.
            return _refreshInFlight ??= RefreshCoreAsync();
        }
    }

    private async Task<bool> RefreshCoreAsync()
    {
        try
        {
            // Uses CancellationToken.None: this refresh is shared by other in-flight requests,
            // so it must not be cancelled just because the request that started it was.
            return await tokenProvider.RefreshAsync(CancellationToken.None);
        }
        finally
        {
            lock (_refreshLock)
            {
                _refreshInFlight = null;
            }
        }
    }
}
