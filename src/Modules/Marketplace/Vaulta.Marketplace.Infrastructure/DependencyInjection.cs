using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Marketplace.Application;

namespace Vaulta.Marketplace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMarketplaceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MarketplaceDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.AddScoped<IMarketplaceStore, MarketplaceStore>();
        services.AddScoped<IMarketplaceQueries, MarketplaceQueries>();
        services.AddScoped<IMarketplaceProductQueries, MarketplaceProductQueries>();
        services.AddScoped<IMarketplaceCatalog, MarketplaceCatalogAdapter>();
        services.AddScoped<IMarketplaceCollection, MarketplaceCollectionAdapter>();
        services.AddScoped<IMarketplaceAssets, MarketplaceAssetsAdapter>();
        services.AddScoped<IMarketplaceReputation, MarketplaceReputationAdapter>();
        services.AddScoped<MarketplaceCommandHandlers>();
        services.AddScoped<ListingPublicationService>();
        services.AddScoped<ListingDraftHandlers>();
        if (configuration.GetValue("Marketplace:CollectionWorkerEnabled", true)) services.AddHostedService<ListingCollectionWorker>();
        return services;
    }
}
