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
            .Validate(o => IsEndpoint(o.ServiceUrl) && IsEndpoint(o.PublicServiceUrl),
                "Assets:S3 endpoints must be absolute HTTP or HTTPS URLs.")
            .ValidateOnStart();
        services.AddSingleton<IAmazonS3>(provider =>
            S3ClientFactory.Create(provider.GetRequiredService<IOptions<S3StorageOptions>>().Value));
        services.AddSingleton<S3PresigningClient>();
        services.AddDbContext<AssetsDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.AddScoped<S3ObjectStorage>();
        services.AddScoped<IObjectStorage>(p=>p.GetRequiredService<S3ObjectStorage>());
        services.AddScoped<IAssetContentStore>(p=>p.GetRequiredService<S3ObjectStorage>());
        services.AddScoped<ISystemAssetService, SystemAssetService>();
        services.AddScoped<IAssetService, AssetService>();
        return services;
    }

    private static bool IsEndpoint(string? value) => string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
}
