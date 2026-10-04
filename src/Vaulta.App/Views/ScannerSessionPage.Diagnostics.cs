#if DEBUG
using System.Text.Json;
namespace Vaulta.App.Views;
public sealed partial class ScannerSessionPage
{
    private object? _lastVisionDiagnostic;
    private async Task ShowVisionDiagnostic()
    {
        RequireOwner();
        if (_lastVisionDiagnostic is null) { await DisplayAlertAsync("Diagnóstico", "Faça uma leitura primeiro.", "Entendi"); return; }
        var snapshot = JsonSerializer.Serialize(_lastVisionDiagnostic, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        var choice = await DisplayActionSheetAsync("Diagnóstico sem fotos", "Voltar", null, "Ver detalhes", "Copiar JSON");
        RequireOwner();
        if (choice == "Ver detalhes") await DisplayAlertAsync("Leitura Vision", snapshot, "Fechar");
        else if (choice == "Copiar JSON") { await Clipboard.Default.SetTextAsync(snapshot); RequireOwner(); _status.Text = "Diagnóstico copiado · sem fotos ou dados da conta."; }
    }
}
#endif
