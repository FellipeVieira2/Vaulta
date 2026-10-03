using System.Globalization;
using System.ComponentModel;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Http;
using Vaulta.App.State;
using Vaulta.Catalog.Contracts;

namespace Vaulta.App.Views;

public sealed partial class ScannerSessionPage : ContentPage
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private readonly SessionState _account;
    private readonly IScannerSessionStore _store;
    private readonly IScannerClient _scanner;
    private readonly ScannerSessionImporter _importer;
    private readonly SessionVideoStore _videoStore;
    private readonly ISessionVideoExporter _videoExporter;
    private readonly Label _total = Text("R$ 0,00", 30, Colors.White, true);
    private readonly Label _cost = Text("", 12, Colors.White);
    private readonly Label _difference = Text("", 13, Colors.White);
    private readonly Label _count = Text("", 12, Colors.White);
    private readonly Label _status = Text("", 14, Colors.White);
    private readonly VerticalStackLayout _result = new() { Spacing = 10 };
    private readonly List<Button> _actions = [];
    private ScannerSession? _session;
    private CameraView? _camera;
    private bool _busy;
    private bool _loaded;
    private bool _visible;
    private long _viewGeneration;
    private CancellationTokenSource _lifetime = new();

    public ScannerSessionPage(SessionState account, IScannerSessionStore store, IScannerClient scanner, ScannerSessionImporter importer,
        SessionVideoStore videoStore, ISessionVideoExporter videoExporter,Vaulta.App.Core.Vision.IVisionClient vision)
    {
        _account = account; _store = store; _scanner = scanner; _importer = importer;
        _videoStore = videoStore; _videoExporter = videoExporter; _vision=vision; _captureArchive=new(vision);
        BackgroundColor = Color.FromArgb("#080911"); Shell.SetNavBarIsVisible(this, false); Shell.SetTabBarIsVisible(this, false);
        Content = new Grid { Children = { Text("Abrindo sua sessão…", 18, Colors.White) } };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing(); _visible = true; var activation = ++_viewGeneration;
        _account.PropertyChanged += AccountChanged;
        if (_lifetime.IsCancellationRequested) { _lifetime.Dispose(); _lifetime = new(); }
        try
        {
            if (_videoLeavingTask is not null) { await _videoLeavingTask; _videoLeavingTask = null; }
            if (!_visible || activation != _viewGeneration) return;
            if (_account.User is not { } owner) { await Shell.Current.GoToAsync(".."); return; }
            if (_session is { } prior && prior.OwnerId != owner.Id)
            {
                _session = null;
                _loaded = false;
                _videoClips = [];
                ClearResults();
            }
            if (!_loaded)
            {
                var restored = await _store.Latest(owner.Id, _lifetime.Token);
                if (!_visible || activation != _viewGeneration || owner.Id != _account.User?.Id) return;
                _session = restored; _loaded = true;
                if (_session is not null) await LoadVideoClips(_lifetime.Token);
                if (!_visible || activation != _viewGeneration || owner.Id != _account.User?.Id) return;
                if (_session is null)
                {
                    _session = ScannerSession.Start(owner.Id, DateTimeOffset.UtcNow);
                    await _store.Save(_session, _lifetime.Token);
                    if (!_visible || activation != _viewGeneration || owner.Id != _account.User?.Id) return;
                    ShowLive();
                }
                else if (_session.Phase == ScannerSessionPhase.Scanning) ShowLive();
                else ShowReview();
            }
            else if (_session is { } existing)
            {
                var saved = await _store.Latest(owner.Id, _lifetime.Token);
                if (!_visible || activation != _viewGeneration || owner.Id != _account.User?.Id) return;
                if (saved?.Id == existing.Id) _session = saved;
                if (_session.Phase == ScannerSessionPhase.Scanning) ShowLive(); else ShowReview();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DetachSharedViews(); ShowError(ex);
            Content = new VerticalStackLayout { Padding = 28, Spacing = 18, Children = { _status, Action("Voltar", () => Shell.Current.GoToAsync("..")) } };
        }
    }

    protected override void OnDisappearing()
    {
        _visible = false; _viewGeneration++; _account.PropertyChanged -= AccountChanged; _lifetime.Cancel(); StopReveal(); StopCamera(); base.OnDisappearing();
    }

    private void ShowSetup()
    {
        StopReveal(); StopCamera(); DetachSharedViews(); _status.Text = "";
        var editing = _session?.Phase == ScannerSessionPhase.Scanning;
        var packs = new Entry { Placeholder = "Quantidade de pacotes (opcional)", Keyboard = Keyboard.Numeric, TextColor = Colors.White,
            Text = editing ? _session!.PackCount?.ToString(CultureInfo.InvariantCulture) : null };
        var cost = new Entry { Placeholder = "Valor em reais (opcional)", Keyboard = Keyboard.Numeric, TextColor = Colors.White,
            Text = editing ? _session!.EnteredCostBrl?.ToString("F2", Br) : null };
        var mode = new Picker { Title = "Custo dos pacotes", ItemsSource = new[] { "Não informar custo", "Valor por pacote", "Valor total" }, SelectedIndex = editing ? (int)_session!.CostMode : 0, TextColor = Colors.White };
        cost.IsVisible = mode.SelectedIndex != 0; mode.SelectedIndexChanged += (_, _) => cost.IsVisible = mode.SelectedIndex != 0;
        var body = new VerticalStackLayout { Padding = 28, Spacing = 22,
            Children = { Text("VAULTA / ABERTURA", 12, Color.FromArgb("#BBA4FF"), true), Text("Sua próxima\ngrande descoberta.", 34, Colors.White, true),
                Text("Mostre o montinho e retire cada carta depois da identificação. A leitura é contínua.", 17, Color.FromArgb("#B4B5C8")),
                Text("O acabamento e a condição são detectados automaticamente pela câmera. Aponte e aguarde o valor.", 12, Color.FromArgb("#B4B5C8")), packs, mode, cost,
                Text("A captura começa automaticamente quando a carta fica enquadrada por um instante.", 13, Color.FromArgb("#BBA4FF")),
                Text("O custo é opcional. Se informar, a sessão mostrará a diferença estimada em relação ao valor das cartas.", 13, Color.FromArgb("#B4B5C8")) } };
        body.Children.Add(Action(editing ? "Salvar e continuar" : "Iniciar sessão", async () =>
        {
            int? count = string.IsNullOrWhiteSpace(packs.Text) ? null : int.TryParse(packs.Text, out var n) ? n : throw new ArgumentException("Informe uma quantidade válida de pacotes.");
            decimal? amount = null;
            if (mode.SelectedIndex != 0)
            {
                var value = cost.Text?.Trim().Replace(',', '.');
                if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)) throw new ArgumentException("Informe o valor sem separador de milhares, como 20,00.");
                amount = parsed;
            }
            var owner = _account.User?.Id ?? throw new InvalidOperationException("Entre na sua conta.");
            if (editing) RequireOwner();
            var started = (editing ? _session! : ScannerSession.Start(owner, DateTimeOffset.UtcNow)).ConfigureCost(count, (PackCostMode)mode.SelectedIndex, amount);
            await _store.Save(started, _lifetime.Token);
            if (!_visible || owner != _account.User?.Id) return;
            _session = started; ShowLive();
        }));
        body.Children.Add(Action(editing ? "Continuar sem alterar" : "Voltar", () => editing ? ReturnToLive() : Shell.Current.GoToAsync(".."))); body.Children.Add(_status); Content = new ScrollView { Content = body };
    }

    private Task CaptureCard() => CaptureCard(false, null);

    private async Task CaptureCard(bool continuous, byte[]? savedFrame)
    {
        var operation = BeginScannerOperation();
        if (!_cameraReady || _camera is null) throw new InvalidOperationException("Aguarde a câmera ou use a busca pelo nome.");
        if (_session?.Phase != ScannerSessionPhase.Scanning) return;
        if (!continuous && Vaulta.App.Services.Camera.CameraSceneSampler.Read(_camera) is { } currentScene) _frameLoop.Consume(currentScene);
        SetIdentificationLoading(true);
        try
        {
            byte[] photo;
            if(savedFrame is not null) photo=savedFrame;
            else
            {
                _status.Text="Tirando a foto…";
                using var stream=await operation.Camera!.CaptureImage(operation.Context.Token);
                using var data=new MemoryStream(); var buffer=new byte[81920]; int read;
                while((read=await stream.ReadAsync(buffer,operation.Context.Token))>0)
                { if(data.Length+read>ScannerClient.MaxImageBytes) throw new InvalidOperationException("A foto ficou muito grande. Tente novamente."); await data.WriteAsync(buffer.AsMemory(0,read),operation.Context.Token); }
                photo=data.ToArray();
            }
            RequireScannerOperation(operation); SetIdentificationLoading(true,captured:true);
            _status.Text="Foto capturada · pode retirar a carta. Identificando…";
            var attempt=await ReserveHistory(operation.Context.Token);RequireScannerOperation(operation);
            var result=await _scanner.ScanCardAsync(photo,null,attempt?.AttemptId,attempt is null?null:Guid.NewGuid().ToString("N"),operation.Context.Token);RequireScannerOperation(operation);
            if(attempt is not null && result.History?.PersistenceStatus=="saved" && (_archiveTask is null || _archiveTask.IsCompleted))
                _archiveTask=_captureArchive.ArchiveAsync(photo,attempt.AttemptId,operation.Context.Token);
            await HandleContinuousResult(result,operation);
        }
        finally { SetIdentificationLoading(false); }
    }

    private async Task Search()
    {
        var operation = BeginScannerOperation();
        var query = await DisplayPromptAsync("Buscar carta", "Digite o nome da carta.", "Buscar", "Cancelar");
        RequireScannerOperation(operation);
        if (string.IsNullOrWhiteSpace(query)) return;
        var result = await _scanner.SearchCardsAsync(query, null, operation.Context.Token); RequireScannerOperation(operation); ShowCandidates(result, requireNumber: false);
    }

    private void ShowCandidates(CardScanResultDto result, bool requireNumber = true)
    {
        ClearResults();
        if (requireNumber && result.VisualIdentification is null && result.Candidates.Count > 0 && result.Candidates.All(x => !x.HasCollectorNumberMatch))
        {
            _status.Text = "Li o nome, mas não confirmei a edição. Aproxime o rodapé com o número da carta ou use a busca.";
            return;
        }
        _status.Text = result.Candidates.Count == 0 && result.VisualIdentification is { } visual
            ? $"Li {DescribeVisual(visual)}, mas ainda não confirmei a edição."
            : result.Candidates.Count == 0 && result.ServiceIssue is not null ? "O serviço de identificação está indisponível. Use a busca pelo nome."
            : result.Candidates.Count == 0 ? "Não confirmei esta edição. Mostre nome e número nítidos ou busque a carta." : "Confira a edição para somar o valor correto.";
        foreach (var candidate in result.Candidates.Where(x => !requireNumber || x.HasCollectorNumberMatch).Take(3).Where(x => x.PrintingId != Guid.Empty))
            _result.Children.Add(Action($"{candidate.Name} · {candidate.SetName} · {candidate.CollectorNumber}", () => ShowCard(candidate.PrintingId, result.VisualIdentification, result.MarketEstimate)));
        if (result.MarketEstimate is { } research && ScannerValuation.ResearchValue(research, result.VisualIdentification, confirmed: true) is { } preview)
        {
            _result.Children.Add(Text($"Estimativa pesquisada: {Money(preview.AmountBrl)} · {research.Confidence:P0} de confiança · {research.CheckedAt.ToLocalTime():dd/MM/yyyy HH:mm}", 12, Colors.White));
            AddResearchSources(_result, research.Sources);
        }
        if (requireNumber && result.VisualIdentification is { } reading && ScannerValuation.IsValidVisual(reading)
            && ScannerAutomaticRecognition.Select(result) is null)
        {
            _result.Children.Add(Text(DescribeVisual(reading), 18, Colors.White, true));
            _result.Children.Add(Text($"{reading.GameCode ?? "TCG"} · {reading.Language ?? "idioma não legível"}", 12, Color.FromArgb("#B4B5C8")));
            AddVisualDetails(reading);
            _result.Children.Add(Text("Edição pendente no catálogo. Confira a leitura antes de somar; estoque e venda aguardam a edição.", 12, Color.FromArgb("#B4B5C8")));
            var operation = BeginScannerOperation();
            _result.Children.Add(Action("Confirmar leitura pendente", () => AddProvisional(result, operation, confirmed: true)));
            _result.Children.Add(Action("Tentar novamente", () => { ClearResults(); _status.Text = "Mostre a carta novamente."; return Task.CompletedTask; }));
        }
        if (_result.Children.Count > 0) _result.Children.Add(Action("Pular carta", () => { ClearResults(); _status.Text = "Mostre a próxima carta."; return Task.CompletedTask; }));
        if (_resultPanel is not null) _resultPanel.IsVisible = _result.Children.Count > 0;
    }

    private async Task ShowCard(Guid printing, CardVisualIdentificationDto? visual = null, ScannerMarketEstimateDto? estimate = null)
    {
        var operation = BeginScannerOperation();
        var details = await _scanner.GetCardDetailsAsync(printing, operation.Context.Token); RequireScannerOperation(operation);
        await AcceptOrConfirmCard(details, visual, operation, estimate, confirmed: true);
    }

    private void ShowCardDetails(ScannerCardDetailsDto details, CardVisualIdentificationDto? visual, ScannerOperation operation)
    {
        RequireScannerOperation(operation); ClearResults();
        var variants = details.Printing.Variants.ToArray();
        var identity = new VerticalStackLayout { Spacing = 5, Children = { Text(details.Printing.CardName, 22, Colors.White, true),
            Text($"{details.Printing.SetName} · {details.Printing.CollectorNumber}", 12, Color.FromArgb("#B4B5C8")) } };
        var heading = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        if (!string.IsNullOrWhiteSpace(details.Printing.ArtworkUrl))
        {
            var artwork = new Image { Source = details.Printing.ArtworkUrl, WidthRequest = 66, HeightRequest = 92, Aspect = Aspect.AspectFit };
            SemanticProperties.SetDescription(artwork, $"Imagem de referência de {details.Printing.CardName}"); heading.Add(artwork);
        }
        heading.Add(identity, 1, 0); _result.Children.Add(heading);
        AddVisualDetails(visual);
        _status.Text = visual?.Certification is not null ? "Carta certificada lida. Cotação da certificação pendente."
            : "Não confirmei o acabamento com 80% de confiança. Confira esta carta.";
        _result.Children.Add(Text(visual?.Certification is not null ? "O preço da carta comum não representa o valor desta certificação."
            : "Confirme o acabamento · referência internacional em reais", 12, Color.FromArgb("#B4B5C8")));
        var scanId = Guid.NewGuid(); var scannedAt = DateTimeOffset.UtcNow;
        var condition = visual?.Certification is not null ? "UNKNOWN" : visual?.Condition ?? "UNKNOWN";
        foreach (var variant in variants)
        {
            var quote = ScannerValuation.ChooseQuote(details, variant.Id, visual);
            _result.Children.Add(Action(quote is null ? $"{variant.Name} · adicionar sem cotação" : $"{variant.Name} · somar {Money(quote.MarketValueBrl)}",
                () => AddIdentifiedCard(details, variant, condition, quote, scanId, scannedAt, operation, visual)));
            if (quote?.AverageMarketValueBrl is { } average)
                _result.Children.Add(Text($"Média {quote.AveragePeriodDays} dias: {Money(average)} · {quote.Source}", 11, Color.FromArgb("#B4B5C8")));
        }
        if (variants.Length == 0) _result.Children.Add(Action("Adicionar sem cotação",
            () => AddIdentifiedCard(details, null, condition, null, scanId, scannedAt, operation, visual)));
        _result.Children.Add(Action("Pular carta", () => { ClearResults(); _status.Text = "Mostre a próxima carta."; return Task.CompletedTask; }));
        if (_resultPanel is not null) _resultPanel.IsVisible = true;
    }

    private void AddVisualDetails(CardVisualIdentificationDto? visual, VerticalStackLayout? target = null)
    {
        target ??= _result;
        if (visual?.Certification is { } label)
            target.Children.Add(Text($"{label.Company ?? "Certificadora ilegível"} · nota {label.Grade ?? "ilegível"} · certificado {label.Number ?? "ilegível"} · leitura visual não verificada", 12, Color.FromArgb("#C8FFDD")));
        if (visual?.SurfaceTreatment is "textured" or "full-art")
            target.Children.Add(Text(visual.SurfaceTreatment == "textured" ? "Superfície texturizada" : "Arte em toda a carta", 12, Color.FromArgb("#B4B5C8")));
        if (visual?.Attributes is { } attributes)
        {
            var text = string.Join(" · ", new[] { attributes.Rarity, attributes.Year?.ToString(), attributes.CardType, attributes.Stage }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (text.Length > 0) target.Children.Add(Text(text, 12, Color.FromArgb("#B4B5C8")));
        }
    }

    private void AddResearchSources(VerticalStackLayout target, IReadOnlyList<ScannerPriceSourceDto>? sources)
    {
        foreach (var source in sources ?? [])
            if (Uri.TryCreate(source.Url, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
                target.Children.Add(Action(string.IsNullOrWhiteSpace(source.Title) ? source.Url : source.Title, () => Launcher.Default.OpenAsync(url)));
    }

    private static string DescribeVisual(CardVisualIdentificationDto visual) => visual.Name
        + (visual.Hp is { } hp ? $" · {hp} PS" : "")
        + (visual.CollectorNumber is { } number ? $" · {number}" : "");

    private async Task Finish()
    {
        await StopRecording();
        RequireOwner(); var completed = _session!.Complete(); await _store.Save(completed, _lifetime.Token); RequireOwner(); _session = completed; StopCamera(); ShowReview();
    }

    private void ShowReview()
    {
        StopReveal(); StopCamera(); DetachSharedViews(); var session = _session!; _status.Text = "";
        var resolvedCount = session.Cards.Count(x => x.PrintingId != Guid.Empty);
        var body = new VerticalStackLayout { Padding = 24, Spacing = 18, Children = { Text("VAULTA / SUA ABERTURA", 12, Color.FromArgb("#BBA4FF"), true),
            Text("Sessão concluída", 30, Colors.White, true), Text(Money(session.EstimatedValueBrl), 38, Colors.White, true),
            Text(session.CostBrl.HasValue ? $"Custo {Money(session.CostBrl.Value)} · diferença estimada {Money(session.EstimatedDifferenceBrl!.Value)}" : "Custo não informado", 16, Colors.White),
            Text($"{session.Cards.Count} cartas · {session.UnpricedCards} sem cotação", 14, Color.FromArgb("#B4B5C8")) } };
        if (session.IsPartialValuation) body.Children.Add(Text("Total parcial: cartas sem cotação não entram na estimativa.", 14, Color.FromArgb("#FFDA77")));
        body.Children.Add(Text("Valores de referência por variante. A condição da carta e o mercado brasileiro podem alterar o preço de venda.", 13, Color.FromArgb("#B4B5C8")));
        if (session.Phase == ScannerSessionPhase.Importing) body.Children.Add(Text($"{session.Cards.Count(x => x.ImportedItemId.HasValue)} de {resolvedCount} cartas resolvidas adicionadas ao estoque.", 14, Color.FromArgb("#C8FFDD")));
        foreach (var card in session.Cards)
        {
            var row = new VerticalStackLayout { Spacing = 4, Children = { Text(card.Name, 17, Colors.White, true), Text($"{card.SetName} · {card.CollectorNumber} · {card.VariantName}", 12, Color.FromArgb("#B4B5C8")),
                Text(card.MarketValue is { } quote ? $"{Money(quote.AmountBrl)} · {quote.Source} · {quote.QuotedAt.ToLocalTime():dd/MM HH:mm}" : "Sem cotação", 14, Colors.White) } };
            AddVisualDetails(card.VisualIdentification, row);
            AddHistoryActions(row,card);
            if (card.PrintingId == Guid.Empty)
                row.Children.Add(Text("Edição pendente no catálogo · estoque e venda indisponíveis", 12, Color.FromArgb("#FFDA77")));
            if (card.MarketValue is { IsEstimate: true } researched)
                row.Children.Add(Text($"Estimativa pesquisada · consultada em {researched.QuotedAt.ToLocalTime():dd/MM/yyyy HH:mm}", 12, Color.FromArgb("#B4B5C8")));
            AddResearchSources(row, card.MarketValue?.Sources);
            if (session.Phase == ScannerSessionPhase.Completed)
                row.Children.Add(Action("Remover da sessão", async () =>
                {
                    RequireOwner(); var removed = _session!.Resume().Remove(card.ScanId);
                    if (removed.Cards.Count > 0) removed = removed.Complete();
                    await _store.Save(removed, _lifetime.Token); RequireOwner(); _session = removed;
                    if (removed.Phase == ScannerSessionPhase.Completed) ShowReview(); else ShowLive();
                }));
            body.Children.Add(Surface(row, "#151726"));
            if (card.PrintingId != Guid.Empty)
                row.Children.Add(Action(card.ListingDraftId.HasValue ? "Retomar anúncio" : "Vender esta carta", () => OpenSale(card.ScanId)));
        }
        if (session.Phase != ScannerSessionPhase.Imported && session.Cards.Any(x => x.PrintingId != Guid.Empty))
            body.Children.Add(Action(session.Phase == ScannerSessionPhase.Importing ? "Retomar adição ao estoque" : "Adicionar cartas resolvidas ao estoque", async () =>
            {
                RequireOwner();
                if (session.Phase == ScannerSessionPhase.Completed && !await DisplayAlertAsync("Adicionar ao estoque?", $"Adicionar as {resolvedCount} cartas com edição resolvida à sua coleção? As pendentes continuam na sessão.", "Adicionar", "Agora não")) return;
                try { _session = await _importer.Import(_session!, _lifetime.Token); }
                finally
                {
                    if (_visible && _session?.OwnerId == _account.User?.Id)
                    {
                        var restored = await _store.Latest(_session!.OwnerId, CancellationToken.None);
                        if (_visible && restored?.OwnerId == _account.User?.Id && restored is not null) { _session = restored; ShowReview(); }
                    }
                }
            }));
        else body.Children.Add(Text(session.Cards.All(x => x.PrintingId == Guid.Empty) ? "Resolva uma edição no catálogo para adicionar ao estoque."
            : "As cartas com edição resolvida foram adicionadas ao estoque.", 16, Color.FromArgb("#C8FFDD")));
        AddVideoReview(body, session);
        if (session.Phase == ScannerSessionPhase.Completed)
            body.Children.Add(Action("Continuar sessão", async () => { RequireOwner(); var resumed = _session!.Resume(); await _store.Save(resumed, _lifetime.Token); RequireOwner(); _session = resumed; ShowLive(); }));
        if (session.Phase != ScannerSessionPhase.Importing) body.Children.Add(Action("Nova sessão", () => { _session = null; ShowSetup(); return Task.CompletedTask; }));
        body.Children.Add(Action("Voltar à coleção", () => Shell.Current.GoToAsync("//main/collection/collection-page"))); body.Children.Add(_status); Content = new ScrollView { Content = body };
    }

    private void UpdateScore()
    {
        var session = _session!; _total.Text = Money(session.EstimatedValueBrl);
        _cost.Text = session.CostBrl.HasValue ? $"/ {Money(session.CostBrl.Value)} investidos" : "estimativa de mercado";
        _count.Text = $"{session.Cards.Count} {(session.Cards.Count == 1 ? "carta" : "cartas")}" + (session.PackCount.HasValue ? $" · {session.PackCount} pacotes" : "");
        if (session.IsPartialValuation) _count.Text += $" · {session.UnpricedCards} sem cotação · total parcial";
        _difference.Text = session.EstimatedDifferenceBrl.HasValue && session.Cards.Count > 0 ? $"{(session.IsPartialValuation ? "Parcial" : "Estimado")}: {Money(session.EstimatedDifferenceBrl.Value)}" : "";
        _difference.TextColor = session.EstimatedDifferenceBrl >= 0 ? Color.FromArgb("#C8FFDD") : Color.FromArgb("#FFB8BE");
        if (_costProgress is not null)
        {
            _costProgress.IsVisible = session.CostBrl is > 0;
            _costProgress.Progress = session.CostBrl is > 0 ? Math.Min(1, (double)(session.EstimatedValueBrl / session.CostBrl.Value)) : 0;
            _costProgress.ProgressColor = session.EstimatedDifferenceBrl >= 0 ? Color.FromArgb("#C8FFDD") : Color.FromArgb("#BBA4FF");
        }
        SemanticProperties.SetDescription(_total, $"Valor estimado da sessão: {Money(session.EstimatedValueBrl)}{(session.IsPartialValuation ? ", total parcial" : "")}");
    }
    private void RequireOwner()
    {
        if (_session is null || _session.OwnerId != _account.User?.Id) throw new InvalidOperationException("Entre na conta que iniciou esta sessão.");
    }
    private sealed record ScannerOperation(ScannerSession Session, CameraView? Camera, ScannerOperationContext Context);
    private ScannerOperation BeginScannerOperation()
    {
        RequireOwner();
        return new(_session!, _camera, new(_session!.Id, _session.OwnerId, _viewGeneration, _lifetime.Token));
    }
    private void RequireScannerOperation(ScannerOperation operation)
    {
        operation.Context.EnsureCurrent(_session, _account.User?.Id, _viewGeneration, _visible);
        if (!ReferenceEquals(operation.Camera, _camera)) throw new OperationCanceledException("A câmera foi substituída.");
    }
    private void AccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionState.User) || _session?.OwnerId == _account.User?.Id) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewGeneration++; _lifetime.Cancel(); StopReveal(); StopCamera(); _session = null; _loaded = false; DetachSharedViews(); _result.Children.Clear();
            Content = new VerticalStackLayout { Padding = 28, Children = { Text("Entre na conta que iniciou a sessão para retomá-la.", 18, Colors.White), Action("Voltar", () => Shell.Current.GoToAsync("..")) } };
        });
    }
    private void DetachSharedViews()
    {
        _actions.Clear();
        foreach (var view in new View[] { _total, _cost, _difference, _count, _status, _result, _revealValue, _videoClock })
        {
            if (view.Parent is Layout layout) layout.Children.Remove(view);
            else if (view.Parent is Border border && border.Content == view) border.Content = null;
            else if (view.Parent is ScrollView scroll && scroll.Content == view) scroll.Content = null;
        }
    }
    private void StopCamera()
    {
        SetIdentificationLoading(false);
        StopContinuous();
        if (_videoClip is not null)
        {
            _videoLeavingTask ??= StopRecordingAfterLeaving(); return;
        }
        DisposeCamera();
    }

    private void DisposeCamera()
    {
        _cameraReady = false;
        var camera = _camera; _camera = null;
        if (camera is null) return;
        try { if (camera.Handler is not null) camera.StopCameraPreview(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Scanner camera stop failed: {ex.GetType().Name}"); }
        finally
        {
            try { camera.Dispose(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Scanner camera dispose failed: {ex.GetType().Name}"); }
        }
    }
    private void ShowError(Exception ex) { _status.Text = ex is ArgumentException or InvalidOperationException ? ex.Message : ApiErrorTranslator.FromException(ex).Message; }
    private Button Action(string title, Func<Task> action)
    {
        var button = new Button { Text = title, CornerRadius = 14, MinimumHeightRequest = 48, Padding = new Thickness(16, 12), BackgroundColor = Color.FromArgb("#282137"), TextColor = Colors.White, FontSize = 14,
            IsEnabled = !_busy };
        _actions.Add(button); SemanticProperties.SetDescription(button, title); UiMotion.AttachPress(button);
        button.Clicked += async (_, _) =>
        {
            if (_busy) return; _busy = true; SetActionsEnabled(false);
            try { await action(); } catch (OperationCanceledException) { } catch (Exception ex) { ShowError(ex); }
            finally { _busy = false; SetActionsEnabled(true); }
        };
        return button;
    }
    private void SetActionsEnabled(bool enabled) { foreach (var button in _actions) button.IsEnabled = enabled; }
    private Task ReturnToLive() { RequireOwner(); ShowLive(); return Task.CompletedTask; }
    private static Label Text(string value, double size, Color color, bool bold = false) => new() { Text = value, FontSize = size, TextColor = color, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };
    private static string Money(decimal value) => value.ToString("C2", Br);
    private static Border Surface(View child, string color) => new() { Content = child, Padding = 14, BackgroundColor = Color.FromArgb(color), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 18 } };
}
