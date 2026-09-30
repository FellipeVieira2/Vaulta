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
        services.AddScoped<OrderCommandHandlers>();
        return services;
    }
}