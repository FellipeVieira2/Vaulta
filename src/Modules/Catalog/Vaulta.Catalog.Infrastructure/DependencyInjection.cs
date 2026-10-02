using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;
using Amazon;
using Amazon.BedrockRuntime;

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
        services.AddOptions<Recognition.NovaScannerOptions>().Bind(configuration.GetSection("Scanner:Nova"))
            .Validate(o => !o.Enabled || o.IsValid(), "Invalid Nova scanner configuration or request limits.")
            .ValidateOnStart();
        if (configuration.GetValue<bool>("Scanner:Nova:Enabled"))
        {
            services.AddSingleton<IAmazonBedrockRuntime>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<Recognition.NovaScannerOptions>>().Value;
                // SDK credential discovery supports workload IAM roles and refreshes credentials.
                // The extractor owns retries within one deadline; avoid multiplying SDK retries.
                return new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig
                {
                    RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
                    Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                    MaxErrorRetry = 0, LogResponse = false, LogMetrics = false
                });
            });
            services.AddSingleton<ICardEvidenceExtractor, Recognition.NovaCardEvidenceExtractor>();
            services.AddScoped<Recognition.CardEvidenceCatalogMatcher>();
            services.AddScoped<ICardRecognitionProvider>(provider => new Recognition.NovaCardRecognitionProvider(
                provider.GetRequiredService<ICardEvidenceExtractor>(),
                provider.GetRequiredService<Recognition.CardEvidenceCatalogMatcher>(),
                provider.GetRequiredService<PokemonTcgRecognitionProvider>()));
        }
        else
            services.AddScoped<ICardRecognitionProvider>(p => p.GetRequiredService<PokemonTcgRecognitionProvider>());
        services.AddScoped<ICardSearchProvider>(p => p.GetRequiredService<PokemonTcgRecognitionProvider>());
        services.AddScoped<IExternalIdResolver, ExternalIdResolver>();
        services.AddSingleton<Recognition.IOcrService, Recognition.TesseractOcrService>();
        services.AddOptions<Recognition.OcrOptions>().Bind(configuration.GetSection("Scanner:Ocr"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ExecutablePath) && o.TimeoutSeconds is >= 1 and <= 120, "Invalid OCR configuration.")
            .ValidateOnStart();
        services.AddScoped<Recognition.FuzzyCardSearchService>();
        services.AddScoped<ScannerService>();
        services.AddMemoryCache();
        services.AddHttpClient<IBrlExchangeRateProvider, BcbExchangeRateProvider>(client =>
        {
            client.BaseAddress = new Uri("https://olinda.bcb.gov.br/olinda/servico/PTAX/versao/v1/odata/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient<IScannerCardDetailsReader, TcgDexScannerDetailsReader>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<TcgDexOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseAddress.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.Timeout);
        });
        services.AddScoped<ICatalogSync, CatalogSyncService>();
        services.AddScoped<CatalogQueries>();
        services.AddScoped<ICatalogSearch>(provider => provider.GetRequiredService<CatalogQueries>());
        services.AddScoped<ICardRecognitionCatalog>(provider => provider.GetRequiredService<CatalogQueries>());
        services.AddScoped<ICatalogCollectionReader>(provider => provider.GetRequiredService<CatalogQueries>());
        return services;
    }
    private static bool IsSupportedLanguage(string language)
    {
        try { TcgDexProvider.NormalizeLanguage(language); return true; }
        catch (CatalogProviderException) { return false; }
    }
}
