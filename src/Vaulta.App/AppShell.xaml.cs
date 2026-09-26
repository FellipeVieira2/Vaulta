using Vaulta.App.Views;
using Vaulta.App.ViewModels;
using Vaulta.App.Services.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;

namespace Vaulta.App;

public partial class AppShell : Shell
{
    private const string HasSeenOnboardingKey = "vaulta.onboarding.seen";
    private readonly IAuthenticationService _authenticationService;
    private readonly ShellContent _startupContent;
    private bool _startupHandled;

    public AppShell(IServiceProvider services, IAuthenticationService authenticationService)
    {
        InitializeComponent();
        var resources = Application.Current!.Resources;
        SetTabBarBackgroundColor(this, (Color)resources["SurfaceDefault"]);
        SetTabBarForegroundColor(this, (Color)resources["BrandPrimary"]);
        SetTabBarTitleColor(this, (Color)resources["BrandPrimary"]);
        SetTabBarUnselectedColor(this, (Color)resources["TextSecondary"]);
        _authenticationService = authenticationService;
        var hasSeenOnboarding = Preferences.Default.Get(HasSeenOnboardingKey, false);
        _startupContent = new ShellContent
        {
            Route = "startup",
            Content = CreatePage(services, hasSeenOnboarding ? "login" : "onboarding-1")
        };
        Items.Add(_startupContent);

        var mainTabs = new TabBar { Route = "main" };
        foreach (var definition in ScreenCatalog.MainTabs)
        {
            var (title, icon) = definition.Id switch
            {
                "home" => ("Início", "tab_home.png"),
                "collection" => ("Coleção", "tab_collection.png"),
                "market" => ("Mercado", "tab_market.png"),
                _ => ("Perfil", "tab_profile.png")
            };
            var section = new ShellSection { Title = title, Route = definition.Id, Icon = icon };
            section.Items.Add(new ShellContent
            {
                Title = title,
                Route = $"{definition.Id}-page",
                Content = CreatePage(services, definition.Id)
            });
            mainTabs.Items.Add(section);
        }
        Items.Add(mainTabs);

        foreach (var definition in ScreenCatalog.All.Where(screen => screen.Id is not ("welcome" or "home" or "collection" or "market" or "profile")))
            Routing.RegisterRoute(definition.Id, typeof(ExperiencePage));
        Routing.RegisterRoute("experience", typeof(ExperiencePage));

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
        if (_startupContent.Content is ExperiencePage page) page.ConfigureScreen("login");
        await GoToAsync("//startup");
    }

    private static ExperiencePage CreatePage(IServiceProvider services, string screenId)
    {
        var page = services.GetRequiredService<ExperiencePage>();
        page.ConfigureScreen(screenId);
        return page;
    }
}
