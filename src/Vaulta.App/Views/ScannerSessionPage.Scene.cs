using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Vaulta.App.Core.Catalog;

namespace Vaulta.App.Views;

public sealed partial class ScannerSessionPage
{
    private bool _cameraReady;
    private bool _soundEnabled = Preferences.Default.Get("scanner-session-sound", true);
    private readonly SessionRevealDrawable _revealDrawing = new();
    private GraphicsView? _revealCanvas;
    private Border? _revealPanel;
    private Border? _lastCardPanel;
    private Border? _resultPanel;
    private ProgressBar? _costProgress;
    private readonly Label _revealTitle = Text("", 12, Color.FromArgb("#FFDA77"), true);
    private readonly Label _revealValue = Text("", 44, Colors.White, true);
    private readonly Label _revealName = Text("", 18, Colors.White, true);
    private readonly Label _lastCard = Text("", 13, Colors.White);

    private void ShowLive()
    {
        StopReveal(); StopCamera(); DetachSharedViews(); ClearResults();
        _status.Text = "Enquadre a carta inteira e evite reflexos.";
        _camera = new CameraView { ImageCaptureResolution = new Size(1920, 1080) };
        var camera = _camera;
        camera.Loaded += async (_, _) =>
        {
            try
            {
                var permission = await Permissions.RequestAsync<Permissions.Camera>();
                if (!_visible || camera != _camera) return;
                if (permission != PermissionStatus.Granted) { _status.Text = "Permita a câmera ou use a busca pelo nome."; return; }
                var available = await camera.GetAvailableCameras(_lifetime.Token);
                if (!_visible || camera != _camera) return;
                camera.SelectedCamera = available.FirstOrDefault(x => x.Position == CameraPosition.Rear) ?? available.FirstOrDefault();
                if (camera.SelectedCamera is null) { _status.Text = "Nenhuma câmera disponível. Use a busca pelo nome."; return; }
                await camera.StartCameraPreview(_lifetime.Token);
                await Vaulta.App.Services.Camera.CameraPreviewReady.Wait(camera, _lifetime.Token);
                if (!_visible || camera != _camera) return;
                _cameraReady = true;
                StartContinuous();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (_visible && camera == _camera) ShowError(ex); }
        };

        var root = new Grid(); root.Children.Add(camera);
        var overlay = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        var top = new Grid { Padding = new Thickness(18, 22, 18, 10), ColumnSpacing = 12,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        var brand = new VerticalStackLayout { Spacing = 5, Children = { Text("VAULTA", 19, Colors.White, true), Text("PACK OPENING", 10, Color.FromArgb("#BBA4FF"), true), _count } };
        var exit = Action("Sair", () => Shell.Current.GoToAsync("..")); exit.FontSize = 12; exit.Padding = new Thickness(10, 4);
        exit.HorizontalOptions = LayoutOptions.Start; brand.Children.Add(exit);
        var score = new VerticalStackLayout { Spacing = 5 };
        var caption = Text("VALOR DAS CARTAS", 9, Color.FromArgb("#BBA4FF"), true); caption.HorizontalTextAlignment = TextAlignment.End;
        _total.FontSize = 28; _total.LineBreakMode = LineBreakMode.NoWrap;
        _total.HorizontalTextAlignment = TextAlignment.End; _cost.HorizontalTextAlignment = TextAlignment.End; _difference.HorizontalTextAlignment = TextAlignment.End;
        score.Children.Add(caption); score.Children.Add(_total); score.Children.Add(_cost); score.Children.Add(_difference);
        _costProgress = new ProgressBar { HeightRequest = 3, BackgroundColor = Color.FromArgb("#40364E") }; score.Children.Add(_costProgress);
        var scorePanel = Surface(score, "#E0080911");
        scorePanel.Stroke = Color.FromArgb("#594476"); scorePanel.StrokeThickness = 1;
        top.Add(brand); top.Add(scorePanel, 1, 0); overlay.Add(top, 0, 0);

        var center = new Grid { InputTransparent = true };
        center.Children.Add(new GraphicsView { Drawable = new SessionFrameDrawable(), InputTransparent = true });
        var revealText = new VerticalStackLayout { Spacing = 6, Children = { _revealTitle, _revealValue, _revealName } };
        _revealTitle.HorizontalTextAlignment = TextAlignment.Center; _revealValue.HorizontalTextAlignment = TextAlignment.Center;
        _revealName.HorizontalTextAlignment = TextAlignment.Center; _revealName.MaxLines = 2;
        _revealPanel = Surface(revealText, "#EF11101D"); _revealPanel.Margin = 24; _revealPanel.VerticalOptions = LayoutOptions.Center;
        _revealPanel.IsVisible = false; _revealPanel.InputTransparent = true;
        _revealPanel.Stroke = Color.FromArgb("#C8B1FF"); _revealPanel.StrokeThickness = 1.5;
        center.Children.Add(_revealPanel); overlay.Add(center, 0, 1);

        var bottom = new VerticalStackLayout { Padding = new Thickness(18, 8, 18, 20), Spacing = 10 };
        _lastCard.MaxLines = 1; _lastCard.LineBreakMode = LineBreakMode.TailTruncation;
        _lastCardPanel = Surface(_lastCard, "#D9111520"); _lastCardPanel.IsVisible = false; bottom.Children.Add(_lastCardPanel);
        var scroll = new ScrollView { Content = _result, MaximumHeightRequest = Math.Clamp(Height * 0.38, 180, 340) };
        _resultPanel = Surface(scroll, "#F0080911"); _resultPanel.IsVisible = false; bottom.Children.Add(_resultPanel);
        _status.FontSize = 12; bottom.Children.Add(Surface(_status, "#CE080911"));
        var reading = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
        Button? automatic = null;
        automatic = Action(_continuousEnabled ? "Pausar leitura" : "Leitura contínua", () =>
        {
            _continuousEnabled = !_continuousEnabled; automatic!.Text = _continuousEnabled ? "Pausar leitura" : "Leitura contínua";
            _status.Text = _continuousEnabled ? "Mostre uma carta e mantenha por um instante." : "Leitura pausada. Use Identificar quando quiser.";
            return Task.CompletedTask;
        });
        reading.Add(automatic); reading.Add(Action("Identificar agora", CaptureCard), 1, 0); bottom.Children.Add(reading);
        var actions = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
        actions.Add(Action("Buscar pelo nome", Search)); actions.Add(Action("Revisar e finalizar", Finish), 1, 0); bottom.Children.Add(actions);
        var utilities = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
        var sound = Action(_soundEnabled ? "Som ligado" : "Som desligado", () =>
        {
            _soundEnabled = !_soundEnabled; Preferences.Default.Set("scanner-session-sound", _soundEnabled);
            return Task.CompletedTask;
        });
        sound.Clicked += (_, _) => { sound.Text = _soundEnabled ? "Som ligado" : "Som desligado"; SemanticProperties.SetDescription(sound, sound.Text); };
        sound.FontSize = 12; sound.BackgroundColor = Colors.Transparent;
        var editCost = Action("Pacotes e custo", async () => { RequireOwner(); await StopRecording(); RequireOwner(); ShowSetup(); }); editCost.FontSize = 12; editCost.BackgroundColor = Colors.Transparent;
        utilities.Add(sound); utilities.Add(editCost, 1, 0); bottom.Children.Add(utilities);
        var recording = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 };
        _recordButton = Action("Gravar com voz e efeitos", ToggleRecording); _recordButton.BackgroundColor = Color.FromArgb("#472339");
        recording.Add(_recordButton); _videoClock.Text = ""; recording.Add(_videoClock, 1, 0); bottom.Children.Add(recording);
        overlay.Add(bottom, 0, 2); root.Children.Add(overlay);
        _revealCanvas = new GraphicsView { Drawable = _revealDrawing, InputTransparent = true }; root.Children.Add(_revealCanvas);
        Content = root; UpdateScore();
        if (_session!.Cards.LastOrDefault() is { } latest) ShowLastCard(latest);
    }

    private void ClearResults()
    {
        _actions.RemoveAll(button => HasAncestor(button, _result));
        _result.Children.Clear();
        if (_resultPanel is not null) _resultPanel.IsVisible = false;
    }

    private static bool HasAncestor(Element element, Element ancestor)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
            if (ReferenceEquals(parent, ancestor)) return true;
        return false;
    }

    private void ShowLastCard(ScannerSessionCard card)
    {
        _lastCard.Text = card.MarketValue is { } value ? $"ÚLTIMA · {card.Name} · {Money(value.AmountBrl)}" : $"ÚLTIMA · {card.Name} · sem cotação";
        if (_lastCardPanel is not null) _lastCardPanel.IsVisible = true;
    }

    private async Task Reveal(ScannerSessionCard card, decimal previousTotal)
    {
        RequireOwner(); var highlight = _session!.IsHighlight(card);
        var session = _session; var token = _lifetime.Token;
        try { HapticFeedback.Default.Perform(highlight ? HapticFeedbackType.LongPress : HapticFeedbackType.Click); } catch (FeatureNotSupportedException) { }
#if ANDROID
        using var tone = _soundEnabled ? new Android.Media.ToneGenerator(Android.Media.Stream.Music, 60) : null;
        tone?.StartTone(highlight ? Android.Media.Tone.PropAck : Android.Media.Tone.PropBeep, highlight ? 450 : 150);
#endif
        _revealTitle.Text = card.MarketValue is null ? "NOVA CARTA" : highlight ? "GRANDE ACHADO" : "CARTA REVELADA";
        _revealValue.Text = card.MarketValue is { } value ? $"+ {Money(value.AmountBrl)}" : "Sem cotação";
        _revealValue.FontSize = card.MarketValue is null ? 28 : 38;
        _revealValue.TextColor = highlight ? Color.FromArgb("#FFDA77") : Color.FromArgb("#C8FFDD");
        _revealName.Text = card.Name;
        var panel = _revealPanel;
        if (panel is null) return;
        panel.IsVisible = true; panel.Opacity = 1;
        _total.TextColor = _revealValue.TextColor;
        _revealDrawing.Highlight = highlight; _revealDrawing.Visible = !UiMotion.ReducedMotion; _revealDrawing.Progress = 0;
        try
        {
            if (!UiMotion.ReducedMotion)
            {
                new Animation(progress =>
                {
                    if (!_visible || _session?.Id != session.Id) return;
                    _total.Text = Money(decimal.Round(previousTotal + (session.EstimatedValueBrl - previousTotal) * (decimal)progress, 2));
                }).Commit(this, "SessionTotal", 16, 700, Easing.CubicOut);
                new Animation(progress => { _revealDrawing.Progress = progress; _revealCanvas?.Invalidate(); })
                    .Commit(this, "SessionConfetti", 16, highlight ? 1450u : 800u, Easing.Linear);
                await panel.ScaleToAsync(highlight ? 1.035 : 1.015, 170, Easing.CubicOut);
                token.ThrowIfCancellationRequested();
                await panel.ScaleToAsync(1, 200, Easing.CubicOut);
            }
            await Task.Delay(highlight ? 1300 : 800, token);
        }
        finally
        {
            StopReveal();
            if (_visible && _session?.Id == session.Id) UpdateScore();
        }
    }

    private void StopReveal()
    {
        this.AbortAnimation("SessionTotal"); this.AbortAnimation("SessionConfetti");
        _revealPanel?.CancelAnimations();
        if (_revealPanel is not null) { _revealPanel.IsVisible = false; _revealPanel.Scale = 1; }
        _revealDrawing.Visible = false; _revealCanvas?.Invalidate(); _total.TextColor = Colors.White; _total.Scale = 1;
    }

    private sealed class SessionFrameDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF bounds)
        {
            var height = Math.Min(bounds.Height * 0.85f, bounds.Width * 0.95f); var width = height * 0.715f;
            if (height < 30) return;
            var left = (bounds.Width - width) / 2; var top = (bounds.Height - height) / 2;
            canvas.StrokeColor = Color.FromArgb("#D2BFFF"); canvas.StrokeSize = 1.2f;
            canvas.Alpha = 0.45f; canvas.DrawRoundedRectangle(left, top, width, height, 18);
            canvas.Alpha = 1; canvas.StrokeSize = 3;
            var corner = Math.Min(22, width * 0.15f);
            foreach (var x in new[] { left, left + width })
                foreach (var y in new[] { top, top + height })
                {
                    canvas.DrawLine(x, y, x + (x == left ? corner : -corner), y);
                    canvas.DrawLine(x, y, x, y + (y == top ? corner : -corner));
                }
        }
    }

    private sealed class SessionRevealDrawable : IDrawable
    {
        public bool Visible { get; set; }
        public bool Highlight { get; set; }
        public double Progress { get; set; }
        public void Draw(ICanvas canvas, RectF bounds)
        {
            if (!Visible || Progress >= 1) return;
            var p = (float)Progress; var color = Highlight ? Color.FromArgb("#FFDA77") : Color.FromArgb("#BBA4FF");
            var centerX = bounds.Width * 0.5f; var centerY = bounds.Height * 0.45f;
            canvas.StrokeColor = color; canvas.StrokeSize = 2; canvas.Alpha = (1 - p) * 0.5f;
            canvas.DrawCircle(centerX, centerY, bounds.Width * (0.18f + p * 0.6f));
            canvas.Alpha = 1 - p; var particles = Highlight ? 36 : 14;
            for (var i = 0; i < particles; i++)
            {
                var angle = i * MathF.PI * 2 / particles;
                var radius = bounds.Width * (0.08f + p * (0.45f + i % 3 * 0.08f));
                var x = centerX + MathF.Cos(angle) * radius;
                var y = centerY + MathF.Sin(angle) * radius * 0.9f + p * p * bounds.Height * 0.17f;
                canvas.FillColor = i % 3 == 0 ? Colors.White : color;
                canvas.FillRoundedRectangle(x, y, i % 2 == 0 ? 4 : 7, i % 2 == 0 ? 10 : 4, 2);
            }
        }
    }
}
