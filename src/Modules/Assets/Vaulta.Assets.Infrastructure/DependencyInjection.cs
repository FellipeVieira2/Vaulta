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
        services.AddOptions<S3StorageOptions>().Bind(configuration.GetSection("Assets:S3"))
            .Validate(o => string.IsNullOrWhiteSpace(o.AccessKey) == string.IsNullOrWhiteSpace(o.SecretKey),
                "Assets:S3:AccessKey and Assets:S3:SecretKey must both be configured or both be absent.")
            .ValidateOnStart();
        services.AddSingleton<IAmazonS3>(provider =>
            S3ClientFactory.Create(provider.GetRequiredService<IOptions<S3StorageOptions>>().Value));
        services.AddDbContext<AssetsDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.AddScoped<IObjectStorage, S3ObjectStorage>();
        services.AddScoped<IAssetService, AssetService>();
        return services;
    }
}
