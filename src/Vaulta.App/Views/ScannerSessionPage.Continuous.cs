using Vaulta.App.Core.Catalog;
using Vaulta.App.Services.Camera;
using Vaulta.Catalog.Contracts;
using Vaulta.Vision.Contracts;

namespace Vaulta.App.Views;

public sealed partial class ScannerSessionPage
{
    private IDispatcherTimer? _continuousTimer;
    private ScannerFrameLoop _frameLoop = new();
    private bool _continuousEnabled = true;
    private bool _continuousInFlight;
    private bool _previewAnalysisInFlight;
    private bool _localVisionAvailable = true;

    private void StartContinuous()
    {
        StopContinuous(); _frameLoop = new();
        _continuousTimer = Dispatcher.CreateTimer();
        _continuousTimer.Interval = TimeSpan.FromMilliseconds(200);
        _continuousTimer.Tick += ContinuousTick; _continuousTimer.Start();
    }
    private void StopContinuous()
    {
        if (_continuousTimer is not { } timer) return;
        timer.Stop(); timer.Tick -= ContinuousTick; _continuousTimer = null;
    }
    private async void ContinuousTick(object? sender, EventArgs e)
    {
        if (!_continuousEnabled || !_visible || !_cameraReady || _camera is null || _session?.Phase != ScannerSessionPhase.Scanning) return;
        var captureStarted=false; var analysisStarted=false; var camera=_camera; var generation=_viewGeneration;
        try
        {
            var blocked=_busy || _continuousInFlight || _previewAnalysisInFlight || _resultPanel?.IsVisible==true;
            var observation=CameraSceneSampler.ReadObservation(camera,!blocked);
            if(observation is null) return;
            if(blocked || !observation.CardPresent)
            {
                _frameLoop.TryBegin(observation.Signature,Environment.TickCount64,observation.CardPresent?ScannerFrameKind.Unknown:ScannerFrameKind.NoCard,false,true);
                if(!blocked) _status.Text="Mostre uma carta · captura automática.";
                return;
            }
            if(observation.Image is null || observation.Sharpness<15) { _status.Text="Aproxime a carta e mostre o nome legível."; return; }
            _previewAnalysisInFlight=true; analysisStarted=true;
            var kind=ScannerFrameKind.Unknown;
            if(_localVisionAvailable)
            {
                try { kind=await LocalCardVision.AnalyzeAsync(observation.Image,_lifetime.Token); }
                catch(OperationCanceledException) { throw; }
                catch(Exception) { _localVisionAvailable=false; } // Local uncertainty must not disable manual/usable-frame capture.
            }
            if(!_visible || camera!=_camera || generation!=_viewGeneration || _lifetime.IsCancellationRequested) return;
            _previewAnalysisInFlight=false; analysisStarted=false;
            if(kind==ScannerFrameKind.Back) _status.Text="Vire a carta para mostrar a frente.";
            else if(kind==ScannerFrameKind.NoCard) _status.Text="Mostre uma carta · captura automática.";
            if(!_frameLoop.TryBegin(observation.Signature,Environment.TickCount64,kind,true,_busy || _resultPanel?.IsVisible==true)) return;
            captureStarted=true; _continuousInFlight=true; _busy=true; SetActionsEnabled(false);
            await CaptureCard(true,observation.FullImage??observation.Image,observation.FullImage is null?null:observation.Image);
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { if(_visible) ShowError(ex); }
        finally
        {
            if(analysisStarted) _previewAnalysisInFlight=false;
            if(captureStarted) { _frameLoop.Complete(); _continuousInFlight=false; _busy=false; SetActionsEnabled(true); }
        }
    }

    private async Task HandleContinuousResult(VisionScanResultDto result, ScannerOperation operation)
    {
        RequireScannerOperation(operation);
        var card=VisionAcceptance.CreateCard(result);
        if(card is not null)
        {
            if(result.PriceStatus=="variant_pending" && (!double.IsFinite(result.VariantConfidence) || result.VariantConfidence<.8))
            { ShowVisionReview(result,operation); return; }
            await SaveReading(card,operation); return;
        }
        if(result.Status is "back" or "not_a_card" or "needs_better_image")
        { ClearResults(); _status.Text=result.Status=="back" ? "Vire a carta para mostrar a frente." : "Mostre a frente da carta com o nome legível."; return; }
        if(result.Candidates.Count==0)
        { ClearResults(); _status.Text="Edição ainda fora do catálogo · tente a busca ou mostre a próxima carta."; return; }
        ShowVisionReview(result,operation);
    }

    private void ShowVisionReview(VisionScanResultDto result,ScannerOperation operation)
    {
        ClearResults(); _status.Text="Não confirmei com 80% de certeza. Confira esta carta.";
        foreach(var candidate in result.Candidates.Take(5))
        {
            var printing=candidate.Printing;
            _result.Children.Add(Text($"{printing.CardName} · {printing.SetName} · {printing.CollectorNumber}",16,Colors.White,true));
            foreach(var variant in printing.Variants.Take(20))
                _result.Children.Add(Action(variant.Name,async () =>
                {
                    RequireScannerOperation(operation);
                    var details=await _scanner.GetCardDetailsAsync(printing.PrintingId,operation.Context.Token); RequireScannerOperation(operation);
                    var quote=result.PriceStatus=="graded_unavailable" ? null : details.MarketQuotes.FirstOrDefault(x=>x.VariantId==variant.Id);
                    var accepted=VisionAcceptance.CreateCard(result,true,printing.PrintingId,variant.Id)!;
                    if(quote is not null) accepted=accepted with {MarketValue=new(decimal.Round(quote.MarketValueBrl,2),quote.Source,quote.UpdatedAt,quote.OriginalValue,quote.OriginalCurrency,quote.ExchangeRate,quote.ExchangeRateAt)};
                    await SaveReading(accepted,operation);
                }));
            if(printing.Variants.Count==0)
                _result.Children.Add(Action("Confirmar sem cotação",()=>SaveReading(VisionAcceptance.CreateCard(result,true,printing.PrintingId)!,operation)));
        }
        _result.Children.Add(Action("Pular carta",()=> { ClearResults(); _status.Text="Mostre a próxima carta."; return Task.CompletedTask; }));
        if(_resultPanel is not null) _resultPanel.IsVisible=true;
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
        if(operation.Session.Cards.Any(x=>x.ScanId==card.ScanId))
        {ClearResults();_status.Text="Essa leitura já está na sessão. Mostre a próxima carta.";return;}
        if(operation.Session.RequiresDuplicateConfirmation(card))
        {
            SetIdentificationLoading(false);
            var again=await DisplayAlertAsync("Carta repetida","Essa carta já foi adicionada. Deseja adicionar novamente?","Adicionar novamente","Cancelar");
            RequireScannerOperation(operation);
            if(!again){ClearResults();_status.Text="Carta não adicionada. Mostre a próxima carta.";return;}
        }
        var previousTotal = operation.Session.EstimatedValueBrl;
        var added = operation.Session.Add(card); await _store.Save(added, operation.Context.Token); RequireScannerOperation(operation); _session = added;
        ClearResults(); _status.Text = card.MarketValue is null ? "Pode retirar a carta · adicionada sem cotação." : "Pode retirar a carta.";
        UpdateScore();
        SetIdentificationLoading(false);
        await Reveal(card, previousTotal); RequireScannerOperation(operation);
        _status.Text = "Mostre a próxima carta · captura automática.";
    }
}
