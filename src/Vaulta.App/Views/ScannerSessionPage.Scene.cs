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
    private bool _revealInFlight;
    private long _revealGeneration;
    private Image? _revealArtwork;
    private Label? _revealTitle;
    private Label? _revealPrinting;
    private Label? _revealSource;
    private Label? _totalDelta;
    private Border? _revealPanel;
    private Border? _resultPanel;
    private ProgressBar? _costProgress;
    private Border? _identificationPanel;
    private ActivityIndicator? _identificationSpinner;
    private Label? _identificationText;
    private readonly Label _revealValue = Text("", 48, Colors.White, true);

    private void ShowLive()
    {
        StopReveal(); StopCamera(); DetachSharedViews(); ClearResults();
        _status.Text = "Mostre uma carta · captura automática.";
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
                _status.Text="Preparando o reconhecimento local…";
                try { await Vaulta.App.Services.Camera.LocalCardVision.WarmupAsync(_lifetime.Token); _localVisionAvailable=true; }
                catch(OperationCanceledException) { throw; }
                catch(Exception) { _localVisionAvailable=false; }
                if (!_visible || camera != _camera) return;
                _status.Text="Mostre uma carta · captura automática.";
                _cameraReady = true;
                StartContinuous();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (_visible && camera == _camera) ShowError(ex); }
        };

        var root = new Grid(); root.Children.Add(camera);
        root.Children.Add(new GraphicsView { Drawable = new SessionFrameDrawable(), InputTransparent = true });
        var overlay = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };

        // Top bar: brand left, score right
        var top = new Grid { Padding = new Thickness(16, 12, 16, 8), ColumnSpacing = 12,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        var brand = new VerticalStackLayout { Spacing = 2 };
        var brandLabel = Text("VAULTA", 18, TokenColor("TextPrimary"), true);
        brandLabel.FontFamily = "InterBold";
        brand.Children.Add(brandLabel);
        var exit = Action("×", () => Shell.Current.GoToAsync(".."));
        exit.FontSize = 20; exit.Padding = new Thickness(12, 6);
        exit.MinimumHeightRequest = 40; exit.MinimumWidthRequest = 40;
        exit.CornerRadius = 20; exit.HorizontalOptions = LayoutOptions.Start;
        exit.SetDynamicResource(Button.BackgroundColorProperty, "SurfaceElevated");
        SemanticProperties.SetDescription(exit, "Fechar scanner");
        brand.Children.Add(exit);

        var score = new VerticalStackLayout { Spacing = 4 };
        _total.FontSize = 32; _total.LineBreakMode = LineBreakMode.NoWrap;
        _total.FontFamily = "InterBold";
        _total.HorizontalTextAlignment = TextAlignment.End;
        _total.SetDynamicResource(Label.TextColorProperty, "TextPrimary");
        _cost.HorizontalTextAlignment = TextAlignment.End;
        _difference.HorizontalTextAlignment = TextAlignment.End;
        score.Children.Add(_total);
        _totalDelta = Text("", 13, TokenColor("StatusSuccess"), true);
        _totalDelta.FontFamily = "InterBold";
        _totalDelta.HorizontalTextAlignment = TextAlignment.End;
        _totalDelta.IsVisible = false;
        score.Children.Add(_totalDelta);
        if (_session!.CostBrl.HasValue) { score.Children.Add(_cost); score.Children.Add(_difference); }
        _costProgress = new ProgressBar { HeightRequest = 3 };
        _costProgress.SetDynamicResource(ProgressBar.ProgressColorProperty, "BrandPrimary");
        _costProgress.SetDynamicResource(VisualElement.BackgroundColorProperty, "SurfaceElevated");
        if (_session.CostBrl.HasValue) score.Children.Add(_costProgress);
        var scorePanel = TokenSurface(score, "SurfaceDefault", 16, 14);
        scorePanel.Stroke = TokenColor("BorderSubtle"); scorePanel.StrokeThickness = 1;
        top.Add(brand); top.Add(scorePanel, 1, 0); overlay.Add(top, 0, 0);

        // Center: identification loading + reveal panel
        var center = new Grid { InputTransparent = true };
        _identificationSpinner = new ActivityIndicator { Color = TokenColor("BrandPrimary"), WidthRequest = 24, HeightRequest = 24 };
        _identificationText = Text("Tirando a foto…", 14, TokenColor("TextPrimary"), true);
        var identifying = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center,
            Children = { _identificationSpinner, _identificationText } };
        _identificationPanel = TokenSurface(identifying, "SurfaceDefault", 20, 14);
        _identificationPanel.Opacity = 0.92;
        _identificationPanel.IsVisible = false;
        _identificationPanel.HorizontalOptions = LayoutOptions.Center;
        _identificationPanel.VerticalOptions = LayoutOptions.End;
        _identificationPanel.Margin = new Thickness(16, 0, 16, 14);
        _identificationPanel.InputTransparent = true;
        SemanticProperties.SetDescription(_identificationPanel, "Identificando esta carta. Aguarde antes de mostrar a próxima.");
        center.Children.Add(_identificationPanel);

        // Reveal panel: artwork left, identity + price right
        _revealValue.HorizontalTextAlignment = TextAlignment.Center;
        _revealValue.FontFamily = "InterBold";
        _revealValue.SetDynamicResource(Label.TextColorProperty, "BrandPrimary");
        _revealArtwork = new Image { WidthRequest = 120, HeightRequest = 168, Aspect = Aspect.AspectFit };
        _revealArtwork.Shadow = new Shadow { Brush = new SolidColorBrush(TokenColor("BrandPrimary")), Opacity = 0.25f, Radius = 20, Offset = new Point(0, 6) };
        _revealTitle = Text("", 24, TokenColor("TextPrimary"), true);
        _revealTitle.FontFamily = "InterBold";
        _revealTitle.LineBreakMode = LineBreakMode.TailTruncation;
        _revealPrinting = Text("", 14, TokenColor("TextSecondary"));
        _revealPrinting.LineBreakMode = LineBreakMode.TailTruncation;
        var identity = new VerticalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center, Children = { _revealTitle, _revealPrinting } };
        var heading = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        heading.Add(_revealArtwork); heading.Add(identity, 1, 0);
        _revealSource = Text("", 12, TokenColor("TextSecondary"));
        _revealSource.HorizontalTextAlignment = TextAlignment.Center;
        _total.HorizontalTextAlignment = TextAlignment.Center;
        var divider = new BoxView { HeightRequest = 1, Margin = new Thickness(0, 8) };
        divider.SetDynamicResource(BoxView.ColorProperty, "BorderSubtle");
        var reveal = new VerticalStackLayout { Spacing = 12, Children = { heading, _revealValue, _revealSource, divider, _total } };
        _revealPanel = TokenSurface(reveal, "SurfaceDefault", 24, 20);
        _revealPanel.Margin = 24; _revealPanel.VerticalOptions = LayoutOptions.Center;
        _revealPanel.IsVisible = false; _revealPanel.InputTransparent = true;
        _revealPanel.Stroke = TokenColor("BorderSubtle"); _revealPanel.StrokeThickness = 1;
        center.Children.Add(_revealPanel); overlay.Add(center, 0, 1);

        // Bottom bar: status, actions, options
        var bottom = new VerticalStackLayout { Padding = new Thickness(16, 6, 16, 12), Spacing = 8 };
        var scroll = new ScrollView { Content = _result, MaximumHeightRequest = 280 };
        _resultPanel = TokenSurface(scroll, "SurfaceDefault", 20, 16);
        _resultPanel.Opacity = 0.95;
        _resultPanel.IsVisible = false;
        _resultPanel.Stroke = TokenColor("BrandPrimary"); _resultPanel.StrokeThickness = 2;
        // Results float over the camera area; opening them must not collapse
        // the guide into the tiny frame observed on a physical phone.
        _resultPanel.VerticalOptions = LayoutOptions.End; _resultPanel.Margin = new Thickness(16, 0, 16, 8);
        overlay.Add(_resultPanel, 0, 1);
        _status.FontSize = 13;
        var statusPanel = TokenSurface(_status, "SurfaceDefault", 14, 10);
        statusPanel.Opacity = 0.85;
        bottom.Children.Add(statusPanel);
        var actions = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 14 };
        var search = Action("Buscar", Search);
        var finish = Action("Finalizar", Finish);
        actions.Add(search); actions.Add(finish, 1, 0); bottom.Children.Add(actions);
        var options = Action("⚙️  Opções", async () =>
        {
            var choice = await DisplayActionSheetAsync("Sessão", "Voltar", null,
                _soundEnabled ? "Desligar som" : "Ligar som", "Pacotes e custo", "Capturar novamente", "Histórico e melhoria", "Nova sessão");
            if (choice is "Desligar som" or "Ligar som")
            { _soundEnabled = !_soundEnabled; Preferences.Default.Set("scanner-session-sound", _soundEnabled); }
            else if (choice == "Pacotes e custo")
            { RequireOwner(); await StopRecording(); RequireOwner(); ShowSetup(); }
            else if (choice == "Histórico e melhoria") await ConfigureHistory();
            else if (choice == "Capturar novamente") await CaptureCard();
            else if (choice == "Nova sessão") { await StopRecording(); RequireOwner(); _session = null; ShowSetup(); }
        });
        options.FontSize = 12; options.MinimumHeightRequest = 40; options.Padding = new Thickness(8, 4);
        options.SetDynamicResource(Button.BackgroundColorProperty, "BackgroundPrimary");
        options.SetDynamicResource(Button.TextColorProperty, "TextSecondary");
        _recordButton = null;
        bottom.Children.Add(options);
#if DEBUG
        bottom.Children.Add(Action("Diagnóstico da leitura", ShowVisionDiagnostic));
#endif
        overlay.Add(bottom, 0, 2); root.Children.Add(overlay);
        Content = root; UpdateScore();
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

    private void SetIdentificationLoading(bool loading, bool captured = false)
    {
        if (_identificationSpinner is not null) _identificationSpinner.IsRunning = loading;
        if (_identificationPanel is not null) _identificationPanel.IsVisible = loading;
        if (_identificationText is not null) _identificationText.Text = captured ? "Foto capturada · identificando…" : "Tirando a foto…";
        if (_identificationPanel is not null) SemanticProperties.SetDescription(_identificationPanel,
            captured ? "Foto capturada. Pode retirar a carta. Identificando; aguarde para iniciar a próxima leitura."
                : "Tirando a foto. Mantenha a carta até a captura terminar.");
    }

    private sealed class SessionFrameDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF bounds)
        {
            var guide = ScannerCaptureGuide.ForPreview(bounds.Width, bounds.Height);
            var height = (float)guide.Height; var width = (float)guide.Width;
            if (height < 60 || width < 40) return;
            var left = (float)guide.Left; var top = (float)guide.Top;
            // Outer guide: subtle BrandPrimary tint
            canvas.StrokeColor = ScannerSessionPage.TokenColor("BrandPrimary"); canvas.StrokeSize = 1.2f;
            canvas.Alpha = 0.2f; canvas.DrawRoundedRectangle(left, top, width, height, 18);
            // Corner accents: brighter BrandPrimary
            canvas.Alpha = 0.6f; canvas.StrokeSize = 3;
            var corner = Math.Min(28, width * 0.18f);
            foreach (var x in new[] { left, left + width })
                foreach (var y in new[] { top, top + height })
                {
                    canvas.DrawLine(x, y, x + (x == left ? corner : -corner), y);
                    canvas.DrawLine(x, y, x, y + (y == top ? corner : -corner));
                }
        }
    }

}