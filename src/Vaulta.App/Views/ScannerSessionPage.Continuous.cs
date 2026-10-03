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

    private void StartContinuous()
    {
        StopContinuous(); _sceneGate = new();
        _continuousTimer = Dispatcher.CreateTimer();
        _continuousTimer.Interval = TimeSpan.FromMilliseconds(250);
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
            _session?.Phase != ScannerSessionPhase.Scanning) return;
        var captureStarted = false;
        try
        {
            var observation = CameraSceneSampler.ReadObservation(_camera);
            if (observation is null) return;
            var signature = observation.Signature;
            var ready = _sceneGate.Observe(signature, Environment.TickCount64, observation.CardPresent);
            // Observe removals while the saved photo is being identified. This
            // rearms another identical copy without starting a concurrent scan.
            if (_busy || _continuousInFlight || _resultPanel?.IsVisible == true) return;
            if (!observation.CardPresent)
            {
                _status.Text = "Enquadre a carta inteira. Se não detectar, use Opções → Capturar novamente.";
                return;
            }
            if (!ready) return;
            _sceneGate.Consume(signature); captureStarted = true; _continuousInFlight = true; _busy = true; SetActionsEnabled(false);
            await CaptureCard(true, signature);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (_visible) ShowError(ex); }
        finally
        {
            // Ticks that only observe motion must not unlock another button action.
            if (captureStarted) { _continuousInFlight = false; _busy = false; SetActionsEnabled(true); }
        }
    }

    private async Task HandleContinuousResult(CardScanResultDto result, ScannerOperation operation)
    {
        var first = ScannerAutomaticRecognition.Select(result);
        if (first is null)
        {
            if (ScannerValuation.CanAutoAcceptVisual(result.VisualIdentification)
                && !ResearchNeedsReview(result.MarketEstimate))
            {
                await AddProvisional(result, operation);
                return;
            }
            ShowCandidates(result);
            return;
        }
        if (result.VisualIdentification is { } uncertain && !ScannerValuation.CanAutoAcceptVisual(uncertain))
        { ShowCandidates(result); return; }
        var details = await _scanner.GetCardDetailsAsync(first.PrintingId, operation.Context.Token); RequireScannerOperation(operation);
        var variant = ScannerValuation.SelectVariant(details, result.VisualIdentification);
        if (ScannerValuation.ChooseQuote(details, variant?.Id, result.VisualIdentification) is null && ResearchNeedsReview(result.MarketEstimate))
        { ShowCandidates(result); return; }
        // Identity belongs to the captured photo. Moving the physical card while
        // awaiting the response must not discard a valid completed recognition.
        var visual = result.VisualIdentification;
        await AcceptOrConfirmCard(details, visual, operation, result.MarketEstimate);
    }

    private static bool ResearchNeedsReview(ScannerMarketEstimateDto? estimate) => estimate is not null
        && (!double.IsFinite(estimate.Confidence) || estimate.Confidence is < .8 or > 1
            || !ScannerValuation.CanAutoAcceptVisual(estimate.Identification));

    private async Task AcceptOrConfirmCard(ScannerCardDetailsDto details, CardVisualIdentificationDto? visual, ScannerOperation operation,
        ScannerMarketEstimateDto? estimate = null, bool confirmed = false)
    {
        RequireScannerOperation(operation);
        var selected = ScannerValuation.SelectVariant(details, visual);
        // The API only exposes finishes meeting the 80% acceptance threshold.
        // Keep confident finishes absent from the catalog without guessing a
        // different priced variant. Only uncertain readings need confirmation.
        if (selected is null && visual?.Finish is null && !ScannerValuation.CanAutoAcceptVisual(visual) && details.Printing.Variants.Count > 0)
        { ShowCardDetails(details, visual, operation); return; }
        var condition = visual?.Certification is not null ? "UNKNOWN" : visual?.Condition ?? "UNKNOWN";
        var quote = ScannerValuation.ChooseQuote(details, selected?.Id, visual);
        await AddIdentifiedCard(details, selected, condition, quote, Guid.NewGuid(), DateTimeOffset.UtcNow, operation, visual, estimate, confirmed);
    }

    private async Task AddIdentifiedCard(ScannerCardDetailsDto details, CatalogVariantDto? variant, string condition,
        CardMarketQuoteDto? quote, Guid scanId, DateTimeOffset scannedAt, ScannerOperation? operation = null, CardVisualIdentificationDto? visual = null,
        ScannerMarketEstimateDto? estimate = null, bool confirmed = false)
    {
        operation ??= BeginScannerOperation(); RequireScannerOperation(operation);
        var card = new ScannerSessionCard(scanId, details.Printing.PrintingId, variant?.Id, details.Printing.CardName,
            details.Printing.SetName, details.Printing.CollectorNumber, variant?.Name ?? visual?.Finish ?? "Sem variante", condition, details.Printing.ArtworkUrl,
            ScannerValuation.SessionValue(details, variant?.Id, visual, estimate, confirmed), scannedAt, VisualIdentification: visual);
        await SaveReading(card, operation);
    }

    private Task AddProvisional(CardScanResultDto result, ScannerOperation operation, bool confirmed = false)
    {
        var visual = result.VisualIdentification ?? throw new InvalidOperationException("Leia a carta novamente.");
        var card = new ScannerSessionCard(Guid.NewGuid(), Guid.Empty, null, visual.Name, visual.SetName ?? "Edição pendente",
            visual.CollectorNumber ?? "", visual.Finish ?? "Acabamento pendente", visual.Certification is not null ? "UNKNOWN" : visual.Condition ?? "UNKNOWN",
            null, ScannerValuation.ResearchValue(result.MarketEstimate, visual, confirmed), DateTimeOffset.UtcNow, VisualIdentification: visual);
        return SaveReading(card, operation);
    }

    private async Task SaveReading(ScannerSessionCard card, ScannerOperation operation)
    {
        RequireScannerOperation(operation);
        var previousTotal = operation.Session.EstimatedValueBrl;
        var added = operation.Session.Add(card); await _store.Save(added, operation.Context.Token); RequireScannerOperation(operation); _session = added;
        ClearResults(); _status.Text = card.MarketValue is null ? "Pode retirar a carta · adicionada sem cotação." : "Pode retirar a carta.";
        UpdateScore();
        SetIdentificationLoading(false);
        await Reveal(card, previousTotal); RequireScannerOperation(operation);
        _status.Text = "Mostre a próxima carta · captura automática.";
    }
}
