using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;

namespace Vaulta.Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CatalogDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.Configure<TcgDexOptions>(configuration.GetSection("Catalog:Providers:TcgDex"));
        services.AddHttpClient<ICatalogProvider, TcgDexProvider>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<TcgDexOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseAddress, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Vaulta-Catalog/1.0");
        });
        services.AddScoped<ICatalogSync, CatalogSyncService>();
        services.AddScoped<ICatalogSearch, CatalogQueries>();
        services.AddScoped<ICatalogCollectionReader>(provider => provider.GetRequiredService<CatalogQueries>());
        return services;
    }
}
