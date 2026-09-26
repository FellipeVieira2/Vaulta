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
    public string Region { get; set; } = "us-east-1";
    public bool ForcePathStyle { get; set; } = true;
}

internal sealed class S3ObjectStorage(IAmazonS3 client, IOptions<S3StorageOptions> options) : IObjectStorage
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
            Expires = DateTime.UtcNow.Add(lifetime)
        };
        return Task.FromResult(client.GetPreSignedURL(request));
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
        return Task.FromResult(client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime)
        }));
    }
}
