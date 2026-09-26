using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vaulta.Assets.Application;

namespace Vaulta.Assets.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAssetsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<S3StorageOptions>(configuration.GetSection("Assets:S3"));
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<S3StorageOptions>>().Value;
            var clientConfig = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
                ForcePathStyle = options.ForcePathStyle
            };
            if (!string.IsNullOrWhiteSpace(options.ServiceUrl)) clientConfig.ServiceURL = options.ServiceUrl;
            var credentials = new BasicAWSCredentials(
                configuration["Assets:S3:AccessKey"] ?? string.Empty,
                configuration["Assets:S3:SecretKey"] ?? string.Empty);
            return new AmazonS3Client(credentials, clientConfig);
        });
        services.AddDbContext<AssetsDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.AddScoped<IObjectStorage, S3ObjectStorage>();
        services.AddScoped<IAssetService, AssetService>();
        return services;
    }
}
