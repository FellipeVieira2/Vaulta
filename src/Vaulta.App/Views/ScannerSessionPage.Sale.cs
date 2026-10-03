using Microsoft.Extensions.DependencyInjection;

namespace Vaulta.App.Views;

public sealed partial class ScannerSessionPage
{
    private async Task OpenSale(Guid scanId)
    {
        var operation = BeginScannerOperation();
        if (operation.Session.Cards.Single(x => x.ScanId == scanId).PrintingId == Guid.Empty)
            throw new InvalidOperationException("Resolva a edição no catálogo antes de vender esta carta.");
        await StopRecording(); RequireScannerOperation(operation);
        var page = Handler!.MauiContext!.Services.GetRequiredService<ScannerSalePage>();
        page.Configure(_session!.Id, scanId); await Navigation.PushAsync(page);
    }
}
