using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Orders.Application;

namespace Vaulta.Orders.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OrdersDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.AddScoped<IOrderStore, OrderStore>();
        services.AddScoped<IOrderQueries, OrderQueries>();
        services.AddScoped<IOrderMarketplace, OrderMarketplaceAdapter>();
        services.AddScoped<IOrderCollection, OrderCollectionAdapter>();
        services.AddScoped<IOrderCollectionStore, OrderCollectionStore>();
        services.AddScoped<OrderCommandHandlers>();
        if (configuration.GetValue("Orders:ReleaseWorkerEnabled", true)) services.AddHostedService<CancelledListingWorker>();
        if (configuration.GetValue("Orders:CollectionWorkerEnabled", true)) services.AddHostedService<DeliveredCollectionWorker>();
        return services;
    }
}
