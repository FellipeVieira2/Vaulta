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
    private readonly Label _revealValue = Text("", 44, Colors.White, true);

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
        var top = new Grid { Padding = new Thickness(16, 12, 16, 8), ColumnSpacing = 12,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        var brand = new VerticalStackLayout { Spacing = 3, Children = { Text("VAULTA", 20, Colors.White, true), Text("SCANNER TCG", 9, Color.FromArgb("#BBA4FF"), true), _count } };
        var exit = Action("×", () => Shell.Current.GoToAsync("..")); exit.FontSize = 24; exit.Padding = new Thickness(10, 2);
        exit.MinimumHeightRequest = 44; exit.HorizontalOptions = LayoutOptions.Start;
        SemanticProperties.SetDescription(exit, "Fechar scanner"); brand.Children.Add(exit);
        var score = new VerticalStackLayout { Spacing = 5 };
        var caption = Text("TOTAL DA SESSÃO", 9, Color.FromArgb("#BBA4FF"), true); caption.HorizontalTextAlignment = TextAlignment.End;
        _total.FontSize = 28; _total.LineBreakMode = LineBreakMode.NoWrap;
        _total.HorizontalTextAlignment = TextAlignment.End; _cost.HorizontalTextAlignment = TextAlignment.End; _difference.HorizontalTextAlignment = TextAlignment.End;
        score.Children.Add(caption); score.Children.Add(_total);
        _totalDelta = Text("", 12, Color.FromArgb("#C8FFDD"), true);
        _totalDelta.HorizontalTextAlignment = TextAlignment.End; _totalDelta.IsVisible = false; score.Children.Add(_totalDelta);
        if (_session!.CostBrl.HasValue) { score.Children.Add(_cost); score.Children.Add(_difference); }
        _costProgress = new ProgressBar { HeightRequest = 3, BackgroundColor = Color.FromArgb("#40364E") };
        if (_session.CostBrl.HasValue) score.Children.Add(_costProgress);
        var scorePanel = Surface(score, "#E0080911");
        scorePanel.Stroke = Color.FromArgb("#594476"); scorePanel.StrokeThickness = 1;
        top.Add(brand); top.Add(scorePanel, 1, 0); overlay.Add(top, 0, 0);

        var center = new Grid { InputTransparent = true };
        _identificationSpinner = new ActivityIndicator { Color = Color.FromArgb("#C8FFDD"), WidthRequest = 22, HeightRequest = 22 };
        _identificationText = Text("Tirando a foto…", 13, Colors.White, true);
        var identifying = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center,
            Children = { _identificationSpinner, _identificationText } };
        _identificationPanel = Surface(identifying, "#E8111520");
        _identificationPanel.IsVisible = false;
        _identificationPanel.HorizontalOptions = LayoutOptions.Center;
        _identificationPanel.VerticalOptions = LayoutOptions.End;
        _identificationPanel.Margin = new Thickness(16, 0, 16, 14);
        _identificationPanel.InputTransparent = true;
        SemanticProperties.SetDescription(_identificationPanel, "Identificando esta carta. Aguarde antes de mostrar a próxima.");
        center.Children.Add(_identificationPanel);
        _revealValue.HorizontalTextAlignment = TextAlignment.Center;
        _revealArtwork = new Image { WidthRequest = 64, HeightRequest = 90, Aspect = Aspect.AspectFit };
        _revealTitle = Text("", 18, Colors.White, true); _revealTitle.FontFamily = "InterSemiBold";
        _revealPrinting = Text("", 12, Color.FromArgb("#B4B5C8"));
        var identity = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center, Children = { _revealTitle, _revealPrinting } };
        var heading = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        heading.Add(_revealArtwork); heading.Add(identity, 1, 0);
        _revealSource = Text("", 11, Color.FromArgb("#B4B5C8")); _revealSource.HorizontalTextAlignment = TextAlignment.Center;
        _revealValue.FontFamily = "InterBold";
        var reveal = new VerticalStackLayout { Spacing = 10, Children = { heading, _revealValue, _revealSource } };
        _revealPanel = Surface(reveal, "#EF11101D"); _revealPanel.Margin = 24; _revealPanel.VerticalOptions = LayoutOptions.Center;
        _revealPanel.IsVisible = false; _revealPanel.InputTransparent = true;
        _revealPanel.Stroke = Color.FromArgb("#C8B1FF"); _revealPanel.StrokeThickness = 1.5;
        center.Children.Add(_revealPanel); overlay.Add(center, 0, 1);

        var bottom = new VerticalStackLayout { Padding = new Thickness(16, 6, 16, 12), Spacing = 7 };
        var scroll = new ScrollView { Content = _result, MaximumHeightRequest = 300 };
        _resultPanel = Surface(scroll, "#F0080911"); _resultPanel.IsVisible = false;
        // Results float over the camera area; opening them must not collapse
        // the guide into the tiny frame observed on a physical phone.
        _resultPanel.VerticalOptions = LayoutOptions.End; _resultPanel.Margin = new Thickness(16, 0, 16, 8);
        overlay.Add(_resultPanel, 0, 1);
        _status.FontSize = 12; bottom.Children.Add(Surface(_status, "#CE080911"));
        var actions = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 14 };
        var search = Action("Buscar", Search); search.BackgroundColor = Colors.Transparent;
        var finish = Action("Finalizar", Finish); finish.BackgroundColor = Colors.Transparent;
        actions.Add(search); actions.Add(finish, 1, 0); bottom.Children.Add(actions);
        var options = Action("Opções da sessão", async () =>
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
        options.FontSize = 11; options.MinimumHeightRequest = 40; options.Padding = new Thickness(8, 4); options.BackgroundColor = Colors.Transparent;
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
        // Draw over the full camera view, the same coordinates the detector samples.
        public void Draw(ICanvas canvas, RectF bounds)
        {
            var guide = ScannerCaptureGuide.ForPreview(bounds.Width, bounds.Height);
            var height = (float)guide.Height; var width = (float)guide.Width;
            if (height < 60 || width < 40) return;
            var left = (float)guide.Left; var top = (float)guide.Top;
            canvas.StrokeColor = Color.FromArgb("#D2BFFF"); canvas.StrokeSize = 1.2f;
            canvas.Alpha = 0.45f; canvas.DrawRoundedRectangle(left, top, width, height, 18);
            canvas.Alpha = 1; canvas.StrokeSize = 3;
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
