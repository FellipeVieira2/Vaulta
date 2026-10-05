using Vaulta.App.Views;
using Vaulta.App.ViewModels;
using Vaulta.App.Services.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;
using Vaulta.App.State;
using Vaulta.App.Views.Components;

namespace Vaulta.App;

public partial class AppShell : Shell
{
    private const string HasSeenOnboardingKey = "vaulta.onboarding.seen";
    private readonly IAuthenticationService _authenticationService;
    private readonly ShellContent _startupContent;
    private readonly SessionState _session;
    private readonly IServiceProvider _services;
    private bool _startupHandled;
    private bool _openingSell;

    public AppShell(IServiceProvider services, IAuthenticationService authenticationService)
    {
        InitializeComponent();
        var resources = Application.Current!.Resources;
        SetTabBarBackgroundColor(this, (Color)resources["SurfaceDefault"]);
        SetTabBarForegroundColor(this, MarketplaceTheme.Brand);
        SetTabBarTitleColor(this, MarketplaceTheme.Brand);
        SetTabBarUnselectedColor(this, (Color)resources["TextSecondary"]);
        _authenticationService = authenticationService;
        _services = services;
        _session = services.GetRequiredService<SessionState>();
        var hasSeenOnboarding = Preferences.Default.Get(HasSeenOnboardingKey, false);
        _startupContent = new ShellContent
        {
            Route = "startup",
            Content = CreatePage(services, hasSeenOnboarding ? "login" : "onboarding-1")
        };
        Items.Add(_startupContent);

        var mainTabs = new TabBar { Route = "main" };
        foreach (var (screenId, title, icon) in new[]
        {
            ("home", "Início", "tab_home.svg"), ("search", "Buscar", "icon_search.svg"),
            ("sell", "Vender", "icon_plus.svg"), ("collection", "Coleção", "tab_collection.svg"), ("profile", "Perfil", "tab_profile.svg")
        })
        {
            var section = new ShellSection { Title = title, Route = screenId, Icon = icon };
            section.Items.Add(new ShellContent
            {
                Title = title,
                Route = $"{screenId}-page",
                ContentTemplate = new DataTemplate(() => CreatePage(services, screenId))
            });
            mainTabs.Items.Add(section);
        }
        Items.Add(mainTabs);

        foreach (var definition in ScreenCatalog.All.Where(screen => screen.Id is not ("welcome" or "home" or "collection" or "profile" or "sell" or "login")))
            Routing.RegisterRoute(definition.Id, typeof(ExperiencePage));
        Routing.RegisterRoute("login", typeof(LoginPage));
        Routing.RegisterRoute("experience", typeof(ExperiencePage));
        Routing.RegisterRoute("scanner-session", typeof(ScannerSessionPage));
        Routing.RegisterRoute("marketplace-listing", typeof(MarketplaceListingDetailPage));
        Routing.RegisterRoute("marketplace-product", typeof(MarketplaceProductDetailPage));
        Routing.RegisterRoute("marketplace-seller", typeof(MarketplaceSellerPage));
        Navigating += SellNavigationRequested;

#if DEBUG
        Routing.RegisterRoute("design-system-gallery", typeof(DesignSystemGalleryPage));
#endif
    }

    public async Task InitializeStartupAsync()
    {
        if (_startupHandled) return;
        _startupHandled = true;

        if (!Preferences.Default.Get(HasSeenOnboardingKey, false))
        {
            return;
        }

        try
        {
            if (await _authenticationService.RestoreSessionAsync())
            {
                await GoToAsync("//main/home/home-page");
                return;
            }
        }
        catch (HttpRequestException)
        {
        }

    }

    public static void CompleteOnboarding() => Preferences.Default.Set(HasSeenOnboardingKey, true);

    public async Task ShowLoginAsync()
    {
        _startupContent.Content = _services.GetRequiredService<LoginPage>();
        await GoToAsync("//startup");
    }

    public async Task OpenSellAsync()
    {
        if (_openingSell) return;
        _openingSell = true;
        try
        {
            if (!_session.IsAuthenticated) { await ShowLoginAsync(); return; }
            await GoToAsync("scanner-session");
        }
        finally { _openingSell = false; }
    }

    private void SellNavigationRequested(object? sender, ShellNavigatingEventArgs args)
    {
        if (!args.Target.Location.OriginalString.Split('/').Any(part => part is "sell" or "sell-page")) return;
        if (!args.CanCancel) return;
        args.Cancel();
        Dispatcher.Dispatch(async () => await OpenSellAsync());
    }

    private static Page CreatePage(IServiceProvider services, string screenId)
    {
        if (screenId == "login") return services.GetRequiredService<LoginPage>();
        if (screenId is "home" or "search")
        {
            var home = services.GetRequiredService<MarketplaceHomePage>();
            if (screenId == "search") home.ConfigureSearch();
            return home;
        }
        if (screenId == "sell") return new ContentPage { Title = "Vender", BackgroundColor = MarketplaceTheme.Background };
        var page = services.GetRequiredService<ExperiencePage>();
        page.ConfigureScreen(screenId);
        return page;
    }
}
