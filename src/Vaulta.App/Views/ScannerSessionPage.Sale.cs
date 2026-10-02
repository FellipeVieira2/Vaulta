using Microsoft.Extensions.DependencyInjection;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Views.Components;

namespace Vaulta.App.Views;

public sealed partial class ScannerSessionPage
{
    private void ShowLiquidityActions(ScannerSessionCard card)
    {
        ClearResults();
        _result.Children.Add(new ScannerResultView(card));
        var actions = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        var sell = Action(card.ListingDraftId.HasValue ? "Retomar anúncio" : "Vender esta carta", () => OpenSale(card.ScanId));
        sell.BackgroundColor = MarketplaceTheme.Brand; sell.TextColor = MarketplaceTheme.Background;
        actions.Add(sell);
        actions.Add(Action(card.ImportedItemId.HasValue ? "Adicionada à coleção" : "Adicionar", async () =>
        {
            var operation = BeginScannerOperation(); var importer = Handler!.MauiContext!.Services.GetRequiredService<ScannerOccurrenceImporter>();
            var imported = await importer.Import(operation.Session, card.ScanId, operation.Context.Token); RequireScannerOperation(operation); _session = imported;
            ShowLiquidityActions(_session.Cards.Single(x => x.ScanId == card.ScanId)); _status.Text = "Carta adicionada à coleção.";
        }), 1, 0);
        _result.Children.Add(actions);
        _result.Children.Add(Action("Próxima carta", () => { ClearResults(); _status.Text = "Retire esta carta e mostre a próxima."; return Task.CompletedTask; }));
        if (_resultPanel is not null) _resultPanel.IsVisible = true;
    }

    private async Task OpenSale(Guid scanId)
    {
        var operation = BeginScannerOperation(); await StopRecording(); RequireScannerOperation(operation);
        var page = Handler!.MauiContext!.Services.GetRequiredService<ScannerSalePage>();
        page.Configure(_session!.Id, scanId); await Navigation.PushAsync(page);
    }
}
