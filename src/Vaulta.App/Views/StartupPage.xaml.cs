using Vaulta.App.ViewModels;

namespace Vaulta.App.Views;

public partial class StartupPage : ContentPage
{
    public StartupPage(StartupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        Shell.SetNavBarIsVisible(this, false);
    }
}
