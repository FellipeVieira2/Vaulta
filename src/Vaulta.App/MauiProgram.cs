using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vaulta.App.Core.Assets;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Collection;
using Vaulta.App.Core.Http;
using Vaulta.App.Services.Api;
using Vaulta.App.Services.Authentication;
using Vaulta.App.State;
using Vaulta.App.ViewModels;
using Vaulta.App.Views;

namespace Vaulta.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
#if VAULTA_INTER_REGULAR
                fonts.AddFont("Inter-Regular.ttf", "InterRegular");
#endif
#if VAULTA_INTER_MEDIUM
                fonts.AddFont("Inter-Medium.ttf", "InterMedium");
#endif
#if VAULTA_INTER_SEMIBOLD
                fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
#endif
#if VAULTA_INTER_BOLD
                fonts.AddFont("Inter-Bold.ttf", "InterBold");
#endif
            });

#if DEBUG
#if IOS
        const string environment = "Development.iOS";
#else
        const string environment = "Development";
#endif
        builder.Logging.AddDebug();
#else
        const string environment = "Production";
#endif

        var configurationResourceName = $"Vaulta.App.Configuration.appsettings.{environment}.json";
        using var configurationStream = typeof(MauiProgram).Assembly.GetManifestResourceStream(configurationResourceName)
            ?? throw new InvalidOperationException($"Missing app configuration resource: {configurationResourceName}.");
        builder.Configuration.AddJsonStream(configurationStream);

        builder.Services.AddOptions<VaultaApiOptions>()
            .Bind(builder.Configuration.GetSection("Api"));

        static Uri ResolveBaseAddress(IServiceProvider serviceProvider)
        {
            var baseUrl = serviceProvider.GetRequiredService<IOptions<VaultaApiOptions>>().Value.BaseUrl;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Configure a valid absolute Api:BaseUrl for this build environment.");

            return new Uri(baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/", UriKind.Absolute);
        }

        builder.Services.AddHttpClient<IVaultaApiClient, VaultaApiClient>((serviceProvider, client) =>
            client.BaseAddress = ResolveBaseAddress(serviceProvider));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SessionState>();
        builder.Services.AddSingleton<ITokenStore, SecureTokenStore>();
        builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
        builder.Services.AddSingleton<IAccessTokenProvider, AppAccessTokenProvider>();
        builder.Services.AddTransient<AuthorizingHttpMessageHandler>();

        // Catalog/Collection/Assets API calls attach a Bearer token and retry once on 401.
        builder.Services.AddHttpClient<ICatalogClient, CatalogClient>((serviceProvider, client) =>
                client.BaseAddress = ResolveBaseAddress(serviceProvider))
            .AddHttpMessageHandler<AuthorizingHttpMessageHandler>();
        builder.Services.AddHttpClient<ICollectionClient, CollectionClient>((serviceProvider, client) =>
                client.BaseAddress = ResolveBaseAddress(serviceProvider))
            .AddHttpMessageHandler<AuthorizingHttpMessageHandler>();

        // AssetClient needs two HttpClients: one authorized (create/confirm) and one bare
        // (the presigned S3/MinIO PUT must carry only the URL's own signature, never our Bearer token).
        builder.Services.AddHttpClient("Vaulta.Assets.Api", (serviceProvider, client) =>
                client.BaseAddress = ResolveBaseAddress(serviceProvider))
            .AddHttpMessageHandler<AuthorizingHttpMessageHandler>();
        builder.Services.AddHttpClient("Vaulta.Assets.PresignedUpload");
        builder.Services.AddTransient<IAssetClient>(serviceProvider =>
        {
            var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            return new AssetClient(factory.CreateClient("Vaulta.Assets.Api"), factory.CreateClient("Vaulta.Assets.PresignedUpload"));
        });

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<ExperiencePage>();
        builder.Services.AddTransient<ExperienceViewModel>();
        builder.Services.AddTransient<StartupPage>();
        builder.Services.AddTransient<StartupViewModel>();
#if DEBUG
        builder.Services.AddTransient<DesignSystemGalleryPage>();
#endif

        return builder.Build();
    }
}
