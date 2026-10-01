using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using Vaulta.Assets.Application;

namespace Vaulta.Assets.Infrastructure;

public sealed class S3StorageOptions
{
    public string Bucket { get; set; } = "vaulta-assets";
    public string? ServiceUrl { get; set; }
    public string? PublicServiceUrl { get; set; }
    public string Region { get; set; } = "us-east-1";
    public bool ForcePathStyle { get; set; } = true;
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}

// Sign against the client-visible endpoint, before signing; rewriting the hostname afterwards invalidates the signature.
internal sealed class S3PresigningClient : IDisposable
{
    private readonly bool _ownsClient;
    public IAmazonS3 Client { get; }
    public Protocol Protocol { get; }

    public S3PresigningClient(IAmazonS3 storageClient, IOptions<S3StorageOptions> options)
    {
        var value = options.Value;
        var endpoint = string.IsNullOrWhiteSpace(value.PublicServiceUrl) ? value.ServiceUrl : value.PublicServiceUrl;
        Protocol = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp
            ? Amazon.S3.Protocol.HTTP : Amazon.S3.Protocol.HTTPS;
        _ownsClient = !string.IsNullOrWhiteSpace(value.PublicServiceUrl);
        Client = _ownsClient ? S3ClientFactory.Create(new S3StorageOptions
        {
            ServiceUrl = value.PublicServiceUrl, Region = value.Region, ForcePathStyle = value.ForcePathStyle,
            AccessKey = value.AccessKey, SecretKey = value.SecretKey, Bucket = value.Bucket
        }) : storageClient;
    }

    public void Dispose() { if (_ownsClient) Client.Dispose(); }
}

internal sealed class S3ObjectStorage(IAmazonS3 client, S3PresigningClient signing, IOptions<S3StorageOptions> options) : IObjectStorage
{
    private readonly string _bucket = options.Value.Bucket;

    public Task<string> CreateUploadUrl(string objectKey, string contentType, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Protocol = signing.Protocol,
            Expires = DateTime.UtcNow.Add(lifetime)
        };
        return Task.FromResult(signing.Client.GetPreSignedURL(request));
    }

    public async Task<StoredObjectInfo?> GetObjectInfo(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = objectKey }, cancellationToken);
            var checksum = await SHA256.HashDataAsync(response.ResponseStream, cancellationToken);
            return new StoredObjectInfo(response.Headers.ContentLength, response.Headers.ContentType, Convert.ToHexString(checksum));
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<string> CreateReadUrl(string objectKey, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(signing.Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Protocol = signing.Protocol,
            Expires = DateTime.UtcNow.Add(lifetime)
        }));
    }
}
