using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Collection.Application;

namespace Vaulta.Collection.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCollectionModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CollectionDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.AddScoped<ICollectionStore, CollectionStore>();
        services.AddScoped<ICollectionCommerceStore, CollectionCommerceStore>();
        services.AddScoped<ICollectionMarketplace, CollectionMarketplaceService>();
        services.AddScoped<ICollectionQueries, CollectionQueries>();
        services.AddScoped<ICollectionValuationSource, CollectionValuationSource>();
        services.AddSingleton(CollectionValuationLimits.Default);
        services.AddScoped<CollectionValuationService>();
        services.AddScoped<ICollectionCatalog, CollectionCatalogAdapter>();
        services.AddScoped<ICollectionAssets, CollectionAssetsAdapter>();
        services.AddScoped<CollectionCommandHandlers>();
        services.AddScoped<CollectionQueryHandlers>();
        return services;
    }
}
