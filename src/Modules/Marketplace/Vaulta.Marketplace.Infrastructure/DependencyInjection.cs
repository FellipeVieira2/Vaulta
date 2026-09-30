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
        services.AddScoped<MarketplaceCommandHandlers>();
        return services;
    }
}