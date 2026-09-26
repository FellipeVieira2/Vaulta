using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vaulta.App.ViewModels;

public partial class StartupViewModel : ObservableObject
{
    [ObservableProperty]
    private string title = "Vaulta";

    [ObservableProperty]
    private string subtitle = "Design System Ready";

#if DEBUG
    [ObservableProperty]
    private bool showGallery = true;
#else
    [ObservableProperty]
    private bool showGallery;
#endif

    [RelayCommand]
    private async Task OpenGalleryAsync() => await Shell.Current.GoToAsync("design-system-gallery");
}
