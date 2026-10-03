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
        services.AddOptions<Recognition.ScannerRecognitionOptions>().Bind(configuration.GetSection("Scanner:Recognition"))
            .Validate(o => o.IsValid(), "Invalid scanner provider/fallback selection.").ValidateOnStart();
        var selection = configuration.GetSection("Scanner:Recognition").Get<Recognition.ScannerRecognitionOptions>() ?? new();
        var primary = selection.Provider ?? (configuration.GetValue<bool>("Scanner:Nova:Enabled") ? "nova" : "ocr");
        var fallback = selection.FallbackProvider == primary ? null : selection.FallbackProvider;
        services.AddOptions<Recognition.NovaScannerOptions>().Bind(configuration.GetSection("Scanner:Nova"))
            .Validate(o => !(o.Enabled || primary == "nova" || fallback == "nova") || o.IsValid(), "Invalid Nova scanner configuration or request limits.")
            .ValidateOnStart();
        services.AddOptions<Recognition.OpenAiScannerOptions>().Bind(configuration.GetSection("Scanner:OpenAI"))
            .PostConfigure(o => o.ApiKey ??= configuration["OPENAI_API_KEY"])
            .Validate(o => o.IsValid(), "Invalid OpenAI scanner configuration or request limits.").ValidateOnStart();
        services.AddScoped<Recognition.CardEvidenceCatalogMatcher>();
        services.AddScoped<IScannerIdentityResolver, Recognition.ScannerIdentityResolver>();
        // Shared evidence extractor is also used by the local-first Vision scanner.
        {
            services.AddHttpClient("Vaulta.Scanner.OpenAI", client =>
            {
                client.BaseAddress = new Uri("https://api.openai.com/v1/");
                client.Timeout = Timeout.InfiniteTimeSpan;
            }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            }).SetHandlerLifetime(Timeout.InfiniteTimeSpan).RemoveAllLoggers();
            services.AddSingleton<Recognition.OpenAiCardEvidenceExtractor>(provider => new(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("Vaulta.Scanner.OpenAI"),
                provider.GetRequiredService<IOptions<Recognition.OpenAiScannerOptions>>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Recognition.OpenAiCardEvidenceExtractor>>()));
        }
        if (primary == "nova" || fallback == "nova")
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
            services.AddSingleton<Recognition.NovaCardEvidenceExtractor>();
        }
        if (primary is "openai" or "nova")
            services.AddSingleton<ICardEvidenceExtractor>(provider => primary == "openai"
                ? provider.GetRequiredService<Recognition.OpenAiCardEvidenceExtractor>() : provider.GetRequiredService<Recognition.NovaCardEvidenceExtractor>());
        services.AddScoped<ICardRecognitionProvider>(provider =>
        {
            _ = provider.GetRequiredService<IOptions<Recognition.ScannerRecognitionOptions>>().Value;
            ICardRecognitionProvider Create(string name, ICardRecognitionProvider? next, bool isPrimary)
            {
                if (name == "ocr")
                {
                    var ocr = provider.GetRequiredService<PokemonTcgRecognitionProvider>();
                    return next is null ? ocr : new Recognition.FallbackCardRecognitionProvider(ocr, next);
                }
                var extractor = isPrimary ? provider.GetRequiredService<ICardEvidenceExtractor>()
                    : name == "openai" ? (ICardEvidenceExtractor)provider.GetRequiredService<Recognition.OpenAiCardEvidenceExtractor>()
                    : provider.GetRequiredService<Recognition.NovaCardEvidenceExtractor>();
                // Compatibility wrapper for legacy callers; same shared matching implementation.
                return name == "nova" && next is not null && fallback == "ocr"
                    ? new Recognition.NovaCardRecognitionProvider(extractor, provider.GetRequiredService<Recognition.CardEvidenceCatalogMatcher>(), next)
                    : new Recognition.EvidenceCardRecognitionProvider(extractor, provider.GetRequiredService<Recognition.CardEvidenceCatalogMatcher>(), next, name, isPrimary ? fallback : null,
                        enricher: provider.GetRequiredService<ICardEvidenceCatalogEnricher>());
            }
            return Create(primary, fallback is null ? null : Create(fallback, null, false), true);
        });
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
        services.AddOptions<MarketResearch.ScannerMarketResearchOptions>().Bind(configuration.GetSection("Scanner:MarketResearch"))
            .Validate(o => o.IsValid(), "Invalid scanner market research limits.").ValidateOnStart();
        services.AddOptions<MarketResearch.JustTcgScannerOptions>().Bind(configuration.GetSection("Scanner:JustTCG"))
            .PostConfigure(o => o.ApiKey ??= configuration["JUSTTCG_API_KEY"])
            .Validate(o => o.TimeoutSeconds is >= 1 and <= 15, "Invalid JustTCG request timeout.").ValidateOnStart();
        services.AddHttpClient("Vaulta.Scanner.Market.OpenAI", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/"); client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan).RemoveAllLoggers();
        services.AddHttpClient("Vaulta.Scanner.Market.JustTCG", client =>
        {
            client.BaseAddress = new Uri("https://api.justtcg.com/v1/"); client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan).RemoveAllLoggers();
        services.AddSingleton<MarketResearch.OpenAiScannerWebMarketResearchProvider>(provider => new(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("Vaulta.Scanner.Market.OpenAI"),
            provider.GetRequiredService<IOptions<Recognition.OpenAiScannerOptions>>(), provider.GetRequiredService<IBrlExchangeRateProvider>(),
            provider.GetRequiredService<Vaulta.SharedKernel.IClock>(), provider.GetRequiredService<IOptions<MarketResearch.ScannerMarketResearchOptions>>()));
        services.AddSingleton<MarketResearch.JustTcgScannerMarketResearchProvider>(provider => new(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("Vaulta.Scanner.Market.JustTCG"),
            provider.GetRequiredService<IOptions<MarketResearch.JustTcgScannerOptions>>(), provider.GetRequiredService<IBrlExchangeRateProvider>(),
            provider.GetRequiredService<Vaulta.SharedKernel.IClock>()));
        services.AddSingleton<IScannerWebMarketResearchProvider, MarketResearch.CompositeScannerWebMarketResearchProvider>();
        services.AddScoped<IScannerMarketResearch, MarketResearch.ScannerMarketResearch>();
        services.AddHttpClient<TcgDexScannerDetailsReader>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<TcgDexOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseAddress.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.Timeout);
        });
        services.AddScoped<IScannerCardDetailsReader, LocalScannerDetailsReader>();
        services.AddHttpClient<Artwork.CatalogArtworkDownloader>(client => client.Timeout=TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(()=>new SocketsHttpHandler { AllowAutoRedirect=false });
        services.AddScoped<ICatalogArtifactImporter, Artwork.CatalogArtifactImporter>();
        services.AddOptions<MarketPriceRefreshOptions>().Bind(configuration.GetSection("Catalog:MarketPrices:Refresh"))
            .Validate(o => o.IsValid(), "Invalid daily market refresh limits.").ValidateOnStart();
        services.AddSingleton<DailyMarketPriceRefreshJob>();
        services.AddHostedService<DailyMarketPriceRefreshWorker>();
        services.AddScoped<CatalogSyncService>();
        services.AddScoped<ICatalogSync>(p => p.GetRequiredService<CatalogSyncService>());
        services.AddScoped<ICatalogDiscoveryImporter>(p => p.GetRequiredService<CatalogSyncService>());
        services.AddHttpClient<ICardEvidenceCatalogEnricher, TcgDexScannerCatalogEnricher>(client =>
        {
            client.BaseAddress = new Uri("https://api.tcgdex.net/v2/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
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
