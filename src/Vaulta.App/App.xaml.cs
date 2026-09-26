using Microsoft.Extensions.DependencyInjection;

namespace Vaulta.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var shell = _services.GetRequiredService<AppShell>();
        MainThread.BeginInvokeOnMainThread(async () => await shell.InitializeStartupAsync());
        return new Window(shell);
    }
}
