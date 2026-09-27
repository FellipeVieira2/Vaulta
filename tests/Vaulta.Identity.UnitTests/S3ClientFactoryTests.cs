using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vaulta.Assets.Application;
using Vaulta.Assets.Infrastructure;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class S3ClientFactoryTests
{
    [Fact]
    public void MinioUsesExplicitBasicCredentialsAndPathStyle()
    {
        var options = new S3StorageOptions
        {
            AccessKey = "local-test-key", SecretKey = "local-test-secret",
            ServiceUrl = "http://minio:9000", ForcePathStyle = true
        };
        using var client = S3ClientFactory.Create(options,
            _ => throw new InvalidOperationException("Unexpected default credential path."),
            (credentials, config) =>
            {
                Assert.IsType<BasicAWSCredentials>(credentials);
                Assert.Equal(options.AccessKey, credentials.GetCredentials().AccessKey);
                Assert.Equal(options.SecretKey, credentials.GetCredentials().SecretKey);
                Assert.True(config.ForcePathStyle);
                Assert.Equal(new Uri(options.ServiceUrl), new Uri(config.ServiceURL));
                return new AmazonS3Client(credentials, config);
            });
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    public void AbsentCredentialsSelectSdkDefaultConstructorWithoutNetwork(string? access, string? secret)
    {
        var options = new S3StorageOptions { AccessKey = access, SecretKey = secret, ForcePathStyle = false };
        var selected = false;
        using var client = S3ClientFactory.Create(options, config =>
        {
            selected = true;
            Assert.False(config.ForcePathStyle);
            Assert.True(string.IsNullOrEmpty(config.ServiceURL));
            Assert.Equal("us-east-1", config.RegionEndpoint.SystemName);
            // Replace only construction so this test never resolves the machine's credentials or IMDS.
            return new AmazonS3Client(new AnonymousAWSCredentials(), config);
        }, (_, _) => throw new InvalidOperationException("Unexpected explicit credential path."));
        Assert.True(selected);
    }

    [Theory]
    [InlineData("sensitive-test-value", null)]
    [InlineData(null, "sensitive-test-value")]
    [InlineData(" ", "sensitive-test-value")]
    public void PartialCredentialPairFailsWithoutDisclosure(string? access, string? secret)
    {
        var error = Assert.Throws<InvalidOperationException>(() => S3ClientFactory.Create(
            new S3StorageOptions { AccessKey = access, SecretKey = secret }));
        Assert.Contains("Assets:S3:AccessKey", error.Message);
        Assert.DoesNotContain("sensitive-test-value", error.Message);
    }

    [Fact]
    public async Task ModuleResolvesLocalCredentialsAndSignsPrivatePutAndGet()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Assets:S3:AccessKey"] = "local-test-key", ["Assets:S3:SecretKey"] = "local-test-secret",
            ["Assets:S3:ServiceUrl"] = "http://minio:9000", ["Assets:S3:ForcePathStyle"] = "true"
        }).Build();
        using var provider = new ServiceCollection().AddAssetsModule(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var put = await storage.CreateUploadUrl("collection-item/test/image", "image/png", TimeSpan.FromMinutes(10), default);
        var get = await storage.CreateReadUrl("collection-item/test/image", TimeSpan.FromMinutes(5), default);
        Assert.Contains("/vaulta-assets/collection-item/test/image", put);
        Assert.Contains("X-Amz-Signature=", put);
        Assert.Contains("X-Amz-Signature=", get);
        Assert.Contains("X-Amz-Expires=300", get);
        Assert.NotEqual(put, get);
    }

    [Fact]
    public async Task TemporaryRoleCredentialsAreIncludedInAwsPresignedUrls()
    {
        using var client = new AmazonS3Client(new SessionAWSCredentials("test-key", "test-secret", "test-session-token"),
            new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.USEast1, ForcePathStyle = false });
        var storage = new S3ObjectStorage(client, Options.Create(new S3StorageOptions
        { Bucket = "vaulta-assets-test", ForcePathStyle = false }));
        foreach (var url in new[]
        {
            await storage.CreateUploadUrl("collection-item/test/image", "image/png", TimeSpan.FromMinutes(10), default),
            await storage.CreateReadUrl("collection-item/test/image", TimeSpan.FromMinutes(5), default)
        })
        {
            Assert.Equal("https", new Uri(url).Scheme);
            Assert.StartsWith("vaulta-assets-test.s3", new Uri(url).Host);
            Assert.Contains("X-Amz-Security-Token=test-session-token", url);
            Assert.Contains("X-Amz-Signature=", url);
        }
    }
}
