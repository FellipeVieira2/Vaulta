using Amazon;
using Amazon.Runtime;
using Amazon.S3;

namespace Vaulta.Assets.Infrastructure;

internal static class S3ClientFactory
{
    public static IAmazonS3 Create(S3StorageOptions options) => Create(options,
        config => new AmazonS3Client(config),
        (credentials, config) => new AmazonS3Client(credentials, config));

    // Constructor delegates let tests observe the selected SDK path without contacting IMDS/AWS.
    internal static IAmazonS3 Create(S3StorageOptions options,
        Func<AmazonS3Config, IAmazonS3> defaultClient,
        Func<AWSCredentials, AmazonS3Config, IAmazonS3> explicitClient)
    {
        var hasAccessKey = !string.IsNullOrWhiteSpace(options.AccessKey);
        var hasSecretKey = !string.IsNullOrWhiteSpace(options.SecretKey);
        if (hasAccessKey != hasSecretKey)
            throw new InvalidOperationException("Assets:S3:AccessKey and Assets:S3:SecretKey must both be configured or both be absent.");

        var config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
            ForcePathStyle = options.ForcePathStyle
        };
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl)) config.ServiceURL = options.ServiceUrl;

        // No empty credentials: the SDK owns discovery and refresh of the EC2 role credentials.
        return hasAccessKey
            ? explicitClient(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config)
            : defaultClient(config);
    }
}
