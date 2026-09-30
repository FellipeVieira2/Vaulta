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
        services.AddOptions<TcgDexOptions>().Bind(configuration.GetSection("Catalog:Providers:TcgDex"))
            .Validate(o => Uri.TryCreate(o.BaseAddress, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps, "TCGdex requires an absolute HTTPS base address.")
            .Validate(o => o.Timeout is >= 1 and <= 300 && o.MaxConcurrency is >= 1 and <= 8 && o.RetryCount is >= 0 and <= 5 && o.MaxRetryDelaySeconds is >= 1 and <= 300, "Invalid TCGdex HTTP limits.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Language) && IsSupportedLanguage(o.Language), "Invalid TCGdex language.")
            .ValidateOnStart();
        services.AddHttpClient<ICatalogProvider, TcgDexProvider>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<TcgDexOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseAddress.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Vaulta-Catalog/1.0");
        });
        services.AddHttpClient<PokemonTcgRecognitionProvider>((_, client) =>
        {
            client.BaseAddress = new Uri("https://api.pokemontcg.io/v2/", UriKind.Absolute);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Vaulta-Scanner/1.0");
        });
        services.AddScoped<ICardRecognitionProvider>(p => p.GetRequiredService<PokemonTcgRecognitionProvider>());
        services.AddScoped<ICardSearchProvider>(p => p.GetRequiredService<PokemonTcgRecognitionProvider>());
        services.AddScoped<ScannerService>();
        services.AddScoped<ICatalogSync, CatalogSyncService>();
        services.AddScoped<CatalogQueries>();
        services.AddScoped<ICatalogSearch>(provider => provider.GetRequiredService<CatalogQueries>());
        services.AddScoped<ICatalogCollectionReader>(provider => provider.GetRequiredService<CatalogQueries>());
        return services;
    }
    private static bool IsSupportedLanguage(string language)
    {
        try { TcgDexProvider.NormalizeLanguage(language); return true; }
        catch (CatalogProviderException) { return false; }
    }
}
