using Vaulta.App.Core.Catalog;
using Vaulta.App.Services.Camera;
using Vaulta.Catalog.Contracts;

namespace Vaulta.App.Views;

public sealed partial class ScannerSessionPage
{
    private IDispatcherTimer? _continuousTimer;
    private ScannerSceneGate _sceneGate = new();
    private bool _continuousEnabled = true;
    private bool _continuousInFlight;
    private string? _continuousVariantCode;

    private void StartContinuous()
    {
        StopContinuous(); _sceneGate = new();
        _continuousTimer = Dispatcher.CreateTimer();
        _continuousTimer.Interval = TimeSpan.FromMilliseconds(600);
        _continuousTimer.Tick += ContinuousTick; _continuousTimer.Start();
    }

    private void StopContinuous()
    {
        if (_continuousTimer is not { } timer) return;
        timer.Stop(); timer.Tick -= ContinuousTick; _continuousTimer = null;
    }

    private async void ContinuousTick(object? sender, EventArgs e)
    {
        if (!_continuousEnabled || !_visible || !_cameraReady || _camera is null ||
            _session?.Phase != ScannerSessionPhase.Scanning || _continuousInFlight) return;
        try
        {
            if (_busy || _resultPanel?.IsVisible == true) return;
            var observation = CameraSceneSampler.ReadObservation(_camera);
            if (observation is null) return;
            var signature = observation.Signature;
            var ready = _sceneGate.Observe(signature, Environment.TickCount64, observation.CardPresent);
            if (!observation.CardPresent)
            {
                _status.Text = "Enquadre a carta inteira. Se não detectar, use Opções → Capturar novamente.";
                return;
            }
            if (!ready) return;
            _sceneGate.Consume(signature); _continuousInFlight = true; _busy = true; SetActionsEnabled(false);
            await CaptureCard(true, signature);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (_visible) ShowError(ex); }
        finally
        {
            // Ticks that only observe motion must not unlock another button action.
            if (_continuousInFlight) { _continuousInFlight = false; _busy = false; SetActionsEnabled(true); }
        }
    }

    private async Task HandleContinuousResult(CardScanResultDto result, byte[] signature, ScannerOperation operation, bool refinement, Guid? previousPrintingId, ScannerRefinementBudget budget)
    {
        var current = _camera is null ? null : CameraSceneSampler.ReadObservation(_camera);
        var sameCard = current is { CardPresent: true } && ScannerSceneGate.IsSameScene(signature, current.Signature);
        var candidates = result.Candidates.OrderByDescending(x => x.ConfidenceScore).ToArray();
        var first = ScannerAutomaticRecognition.Select(result, previousPrintingId);
        if (first is null && sameCard && !refinement && budget.TryRefine(result))
        {
            _status.Text = "Validando a leitura… mantenha a carta.";
            await Task.Delay(250, operation.Context.Token); RequireScannerOperation(operation);
            current = _camera is null ? null : CameraSceneSampler.ReadObservation(_camera);
            if (!_visible || current is null || !current.CardPresent || !ScannerSceneGate.IsSameScene(signature, current.Signature))
            { ShowCandidates(result); return; }
            await CaptureCard(true, signature, refinement: true, candidates.FirstOrDefault()?.PrintingId, budget);
            return;
        }
        if (candidates.Length == 0)
        {
            _status.Text = "Não consegui ler. Ajuste a carta e mantenha por um instante.";
            return;
        }
        if (first is null) { ShowCandidates(result); return; }
        var details = await _scanner.GetCardDetailsAsync(first.PrintingId, operation.Context.Token); RequireScannerOperation(operation);
        // Identity belongs to the captured photo. Moving the physical card while
        // awaiting the response must not discard a valid completed recognition.
        var variants = details.Printing.Variants;
        var selected = _continuousVariantCode is { } code ? variants.SingleOrDefault(x => x.Code == code)
            : variants.Count == 1 ? variants[0] : null;
        if (selected is null && variants.Count > 0) { await ShowCard(first.PrintingId); return; }
        var quote = ChooseQuote(details, selected?.Id);
        await AddIdentifiedCard(details, selected, "UNKNOWN", quote, Guid.NewGuid(), DateTimeOffset.UtcNow, operation);
    }

    private static CardMarketQuoteDto? ChooseQuote(ScannerCardDetailsDto details, Guid? variantId) =>
        details.MarketQuotes.Where(x => x.VariantId == variantId)
            .OrderBy(x => x.Source.Contains("TCGplayer", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();

    private async Task AddIdentifiedCard(ScannerCardDetailsDto details, CatalogVariantDto? variant, string condition,
        CardMarketQuoteDto? quote, Guid scanId, DateTimeOffset scannedAt, ScannerOperation? operation = null)
    {
        operation ??= BeginScannerOperation(); RequireScannerOperation(operation);
        var card = new ScannerSessionCard(scanId, details.Printing.PrintingId, variant?.Id, details.Printing.CardName,
            details.Printing.SetName, details.Printing.CollectorNumber, variant?.Name ?? "Sem variante", condition, details.Printing.ArtworkUrl,
            quote is null ? null : new(decimal.Round(quote.MarketValueBrl, 2), quote.Source, quote.UpdatedAt,
                quote.OriginalValue, quote.OriginalCurrency, quote.ExchangeRate, quote.ExchangeRateAt), scannedAt);
        var previousTotal = operation.Session.EstimatedValueBrl;
        var added = operation.Session.Add(card); await _store.Save(added, operation.Context.Token); RequireScannerOperation(operation); _session = added;
        ClearResults(); _status.Text = quote is null ? "Pode retirar a carta · adicionada sem cotação." : "Pode retirar a carta.";
        UpdateScore(); ShowLastCard(card); await RecordVideoReveal(card); RequireScannerOperation(operation);
        SetIdentificationLoading(false);
        await Reveal(card, previousTotal); RequireScannerOperation(operation); ShowLiquidityActions(card);
    }
}
