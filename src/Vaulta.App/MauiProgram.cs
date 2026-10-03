using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vaulta.App.Core.Assets;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Collection;
using Vaulta.App.Core.Http;
using Vaulta.App.Core.Marketplace;
using Vaulta.App.Core.Orders;
using Vaulta.App.Core.Payments;
using Vaulta.App.Core.Address;
using Vaulta.App.Services.Api;
using Vaulta.App.Services.Authentication;
using Vaulta.App.Services.Camera;
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
            .UseMauiCommunityToolkitCamera()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Inter-Regular.ttf", "InterRegular");
                fonts.AddFont("Inter-Medium.ttf", "InterMedium");
                fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
                fonts.AddFont("Inter-Bold.ttf", "InterBold");
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
#if DEBUG
        var developmentUrl = typeof(MauiProgram).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(x => x.Key == "VaultaDevelopmentApiBaseUrl")?.Value;
        if (!string.IsNullOrWhiteSpace(developmentUrl))
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Api:BaseUrl"] = developmentUrl });
#endif

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
        builder.Services.AddSingleton<Vaulta.App.Core.Identity.AuthenticationCommitCoordinator>();
        builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
        builder.Services.AddSingleton<IAccessTokenProvider, AppAccessTokenProvider>();
        builder.Services.AddTransient<AuthorizingHttpMessageHandler>();

        builder.Services.AddHttpClient<IScannerClient, ScannerClient>((serviceProvider, client) =>
                client.BaseAddress = ResolveBaseAddress(serviceProvider))
            .AddHttpMessageHandler<AuthorizingHttpMessageHandler>();

        builder.Services.AddHttpClient<Vaulta.App.Core.Vision.IVisionClient,Vaulta.App.Core.Vision.VisionClient>((serviceProvider,client)=>client.BaseAddress=ResolveBaseAddress(serviceProvider))
            .AddHttpMessageHandler<AuthorizingHttpMessageHandler>();
        // Catalog/Collection/Assets API calls attach a Bearer token and retry once on 401.
        builder.Services.AddHttpClient<ICatalogClient, CatalogClient>((serviceProvider, client) =>
                client.BaseAddress = ResolveBaseAddress(serviceProvider))
            .AddHttpMessageHandler<AuthorizingHttpMessageHandler>();
        builder.Services.AddHttpClient<ICollectionClient, CollectionClient>((serviceProvider, client) =>
                client.BaseAddress = ResolveBaseAddress(serviceProvider))
            .AddHttpMessageHandler<AuthorizingHttpMessageHandler>();
        builder.Services.AddHttpClient<IMarketplaceClient, MarketplaceClient>((serviceProvider, client) =>
                client.BaseAddress = ResolveBaseAddress(serviceProvider))
            .AddHttpMessageHandler<AuthorizingHttpMessageHandler>();
        builder.Services.AddHttpClient<IOrdersClient, OrdersClient>((serviceProvider, client) =>
                client.BaseAddress = ResolveBaseAddress(serviceProvider))
            .AddHttpMessageHandler<AuthorizingHttpMessageHandler>();
        builder.Services.AddHttpClient<IPaymentsClient, PaymentsClient>((serviceProvider, client) =>
                client.BaseAddress = ResolveBaseAddress(serviceProvider))
            .AddHttpMessageHandler<AuthorizingHttpMessageHandler>();
        builder.Services.AddHttpClient<IPayoutsClient, PayoutsClient>((serviceProvider, client) =>
                client.BaseAddress = ResolveBaseAddress(serviceProvider))
            .AddHttpMessageHandler<AuthorizingHttpMessageHandler>();
        builder.Services.AddHttpClient<IViaCepClient, ViaCepClient>(client =>
                client.BaseAddress = new Uri("https://viacep.com.br/ws/"));

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

        // Charts remain unavailable until the backend exposes measured BRL snapshots.
        builder.Services.AddSingleton<IPriceHistoryProvider, UnavailablePriceHistoryProvider>();

        builder.Services.AddSingleton<ICameraService, CameraService>();
        builder.Services.AddHttpClient<IListingDraftClient, ListingDraftClient>((serviceProvider, client) =>
        {
            client.BaseAddress = new Uri(serviceProvider.GetRequiredService<IOptions<VaultaApiOptions>>().Value.BaseUrl);
        }).AddHttpMessageHandler<AuthorizingHttpMessageHandler>();
        builder.Services.AddSingleton<IScannerSessionStore>(_ => new FileScannerSessionStore(Path.Combine(FileSystem.AppDataDirectory, "scanner-sessions")));
        builder.Services.AddSingleton<ScannerOccurrenceImporter>(sp => new(sp.GetRequiredService<ICollectionClient>(), sp.GetRequiredService<IScannerSessionStore>(),
            () => sp.GetRequiredService<SessionState>().User?.Id));
        builder.Services.AddSingleton<ScannerSaleFlow>(sp => new(sp.GetRequiredService<ScannerOccurrenceImporter>(), sp.GetRequiredService<ICollectionClient>(),
            sp.GetRequiredService<IListingDraftClient>(), sp.GetRequiredService<IScannerSessionStore>(), () => sp.GetRequiredService<SessionState>().User?.Id));
        builder.Services.AddTransient<ScannerSaleViewModel>();
        builder.Services.AddTransient<ScannerSalePage>();
        builder.Services.AddTransient<MarketplaceHomeController>(sp => new(sp.GetRequiredService<IMarketplaceClient>(),
            () => Connectivity.Current.NetworkAccess == NetworkAccess.Internet));
        builder.Services.AddTransient<MarketplaceHomeViewModel>();
        builder.Services.AddTransient<MarketplaceHomePage>();
        builder.Services.AddTransient<MarketplaceListingDetailController>();
        builder.Services.AddTransient<MarketplaceListingDetailViewModel>();
        builder.Services.AddTransient<MarketplaceListingDetailPage>();
        builder.Services.AddTransient<MarketplaceSellerViewModel>();
        builder.Services.AddTransient<MarketplaceSellerPage>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<ScannerSessionImporter>(sp => new(sp.GetRequiredService<ICollectionClient>(), sp.GetRequiredService<IScannerSessionStore>(),
            () => sp.GetRequiredService<SessionState>().User?.Id));
        builder.Services.AddTransient<ScannerSessionPage>();
        builder.Services.AddSingleton(_ => new SessionVideoStore(Path.Combine(FileSystem.AppDataDirectory, "session-videos"), FileSystem.CacheDirectory));
#if ANDROID
        builder.Services.AddSingleton<ISessionVideoExporter, AndroidSessionVideoExporter>();
#endif
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
