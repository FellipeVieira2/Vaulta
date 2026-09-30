using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Wallets.Application;

namespace Vaulta.Wallets.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWalletsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<WalletsDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.AddScoped<IWalletStore, WalletStore>();
        services.AddScoped<IWalletLedgerQueries, WalletLedgerQueries>();
        services.AddScoped<WalletCommandHandlers>();
        services.AddScoped<Vaulta.Identity.Application.IEventConsumer, UserRegisteredWalletConsumer>();
        return services;
    }
}