using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Shipping.Application;

namespace Vaulta.Shipping.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddShippingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ShippingDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.AddScoped<IShippingStore, ShippingStore>();
        services.AddScoped<IShippingOrders, ShippingOrdersAdapter>();
        services.AddScoped<ShippingCommandHandlers>();
        return services;
    }
}