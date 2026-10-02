using Microsoft.Extensions.DependencyInjection;

namespace Vaulta.App.Views;

public sealed partial class ScannerSessionPage
{
    private async Task OpenSale(Guid scanId)
    {
        var operation = BeginScannerOperation(); await StopRecording(); RequireScannerOperation(operation);
        var page = Handler!.MauiContext!.Services.GetRequiredService<ScannerSalePage>();
        page.Configure(_session!.Id, scanId); await Navigation.PushAsync(page);
    }
}
