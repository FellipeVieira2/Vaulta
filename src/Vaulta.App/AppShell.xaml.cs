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
    private readonly IServiceProvider _services;
    private readonly ShellContent _startupContent;
    private bool _startupHandled;

    public AppShell(IServiceProvider services, IAuthenticationService authenticationService)
    {
        InitializeComponent();
        _authenticationService = authenticationService;
        _services = services;
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
            var section = new ShellSection { Title = definition.Title, Route = definition.Id };
            section.Items.Add(new ShellContent
            {
                Title = definition.Title,
                Route = $"{definition.Id}-page",
                Content = CreatePage(services, definition.Id)
            });
            mainTabs.Items.Add(section);
        }
        Items.Add(mainTabs);

        foreach (var definition in ScreenCatalog.All.Where(screen => screen.Id is not ("welcome" or "home" or "collection" or "market" or "profile")))
            Routing.RegisterRoute(definition.Id, typeof(ExperiencePage));
        Routing.RegisterRoute("experience", typeof(ExperiencePage));
        Routing.RegisterRoute("onboarding-2", typeof(ExperiencePage));

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
            Preferences.Default.Set(HasSeenOnboardingKey, true);
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

    private static ExperiencePage CreatePage(IServiceProvider services, string screenId)
    {
        var page = services.GetRequiredService<ExperiencePage>();
        page.ConfigureScreen(screenId);
        return page;
    }
}
