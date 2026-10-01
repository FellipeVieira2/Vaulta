using System.Diagnostics;
using CommunityToolkit.Maui.Views;
using Vaulta.App.Core.Catalog;

namespace Vaulta.App.Views;

public sealed partial class ScannerSessionPage
{
    private readonly SemaphoreSlim _videoGate = new(1, 1);
    private readonly Stopwatch _videoWatch = new();
    private readonly Label _videoClock = Text("", 13, Color.FromArgb("#FFB8BE"), true);
    private IReadOnlyList<SessionVideoClip> _videoClips = [];
    private SessionVideoClip? _videoClip;
    private FileStream? _videoStream;
    private CameraView? _recordingCamera;
    private IDispatcherTimer? _videoTimer;
    private Button? _recordButton;
    private Task? _videoLeavingTask;

    private async Task LoadVideoClips(CancellationToken ct)
    {
        var session = _session!; var clips = await _videoStore.Get(session.OwnerId, session.Id, ct);
        if (_session?.Id == session.Id && session.OwnerId == _account.User?.Id) _videoClips = clips;
    }

    private async Task ToggleRecording()
    {
        if (_videoClip is not null) { await StopRecording(); return; }
        RequireOwner();
        if (!_cameraReady || _camera is null) throw new InvalidOperationException("Aguarde a câmera antes de gravar.");
#if ANDROID
        if (new Android.OS.StatFs(FileSystem.CacheDirectory).AvailableBytes < 256L * 1024 * 1024)
            throw new InvalidOperationException("Libere espaço no aparelho antes de iniciar a gravação.");
#endif
        if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
            throw new InvalidOperationException("Permita o microfone para gravar sua voz junto com a abertura.");
        if (!_visible) return; RequireOwner();
        await _videoGate.WaitAsync(_lifetime.Token);
        try
        {
            var session = _session!;
            var clip = new SessionVideoClip(Guid.NewGuid(), session.OwnerId, session.Id, DateTimeOffset.UtcNow, SessionVideoState.Recording,
                !UiMotion.ReducedMotion, [SessionVideoClip.Snapshot(session, 0)]);
            await _videoStore.Save(clip, _lifetime.Token); RequireOwner();
            _videoStream = new FileStream(_videoStore.RawPath(clip), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            _videoClip = clip; _recordingCamera = _camera!;
            var existingCaptures = Directory.GetFiles(FileSystem.CacheDirectory, "*.mp4").Select(Path.GetFileName).ToHashSet();
            await _recordingCamera.StartVideoRecording(_videoStream, CancellationToken.None);
            _videoWatch.Restart();
            var captures = Directory.GetFiles(FileSystem.CacheDirectory, "*.mp4").Select(Path.GetFileName)
                .Where(x => !existingCaptures.Contains(x) && SessionVideoStore.IsCaptureFileName(x)).ToArray();
            if (captures.Length == 1)
            {
                _videoClip = clip with { CaptureCacheFileName = captures[0] };
                await _videoStore.Save(_videoClip, CancellationToken.None);
            }
            else throw new InvalidOperationException("A câmera não iniciou a gravação. Aguarde a imagem e tente novamente.");
            _videoTimer = Dispatcher.CreateTimer(); _videoTimer.Interval = TimeSpan.FromSeconds(1);
            _videoTimer.Tick += VideoTick; _videoTimer.Start();
            _recordButton!.Text = "Parar e salvar gravação"; _videoClock.Text = "● 00:00";
            _status.Text = "Gravando sua voz. Revele as cartas normalmente; os valores e efeitos entram no vídeo final.";
        }
        catch
        {
            if (_recordingCamera is not null)
            {
                try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await _recordingCamera.StopVideoRecording<Stream>(timeout.Token); }
                catch (Exception ex) { Debug.WriteLine($"Failed recording start cleanup: {ex.GetType().Name}"); }
            }
            try
            {
                if (_videoClip is { } clip) await _videoStore.Save(clip with { State = SessionVideoState.Interrupted }, CancellationToken.None);
            }
            finally
            {
                _videoStream?.Dispose(); _videoStream = null; _videoClip = null; _recordingCamera = null;
                DisposeCamera(); if (_visible) ShowLive();
            }
            throw;
        }
        finally { _videoGate.Release(); }
        if (!_visible || _videoClip?.OwnerId != _account.User?.Id)
        {
            await StopRecording(); DisposeCamera();
        }
    }

    private async void VideoTick(object? sender, EventArgs e)
    {
        if (_videoClip is null) return;
        _videoClock.Text = $"● {(int)_videoWatch.Elapsed.TotalMinutes:00}:{_videoWatch.Elapsed.Seconds:00}";
        var lowSpace = false;
#if ANDROID
        lowSpace = new Android.OS.StatFs(FileSystem.CacheDirectory).AvailableBytes < 128L * 1024 * 1024;
#endif
        if (_videoWatch.Elapsed < TimeSpan.FromMinutes(30) && !lowSpace) return;
        _videoTimer?.Stop();
        try { await StopRecording(); if (_visible) _status.Text = lowSpace ? "Gravação encerrada por falta de espaço. Libere espaço antes de exportar." : "Clipe de 30 minutos salvo. Você pode iniciar outra gravação na mesma sessão."; }
        catch (Exception ex) { if (_visible) ShowError(ex); }
    }

    private async Task RecordVideoReveal(ScannerSessionCard card)
    {
        await _videoGate.WaitAsync(_lifetime.Token);
        try
        {
            if (_videoClip is not { } clip || clip.SessionId != _session!.Id) return;
            _videoClip = clip.AddSnapshot(SessionVideoClip.Snapshot(_session!, _videoWatch.Elapsed.Ticks / 10, card, _soundEnabled));
            await _videoStore.Save(_videoClip, CancellationToken.None);
        }
        finally { _videoGate.Release(); }
    }

    private async Task StopRecording()
    {
        await _videoGate.WaitAsync();
        try
        {
            if (_videoClip is not { } clip) return;
            if (_videoTimer is { } timer) { timer.Stop(); timer.Tick -= VideoTick; _videoTimer = null; }
            _videoWatch.Stop();
            clip = clip with { CaptureDurationUs = _videoWatch.Elapsed.Ticks / 10 };
            try
            {
                // Page cancellation must not abort MP4 finalization and leave an unusable recording.
                using var finishTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await _recordingCamera!.StopVideoRecording(finishTimeout.Token);
                await _videoStream!.FlushAsync(CancellationToken.None);
                if (_videoStream.Length == 0) throw new InvalidOperationException("A câmera não conseguiu salvar a filmagem. Tente gravar novamente.");
                var saved = clip with { State = SessionVideoState.ReadyToExport };
                await _videoStore.Save(saved, CancellationToken.None);
                _videoClips = _videoClips.Where(x => x.Id != saved.Id).Append(saved).ToArray();
                if (_visible && clip.OwnerId == _account.User?.Id) _status.Text = "Gravação salva neste aparelho. Exporte com valores e efeitos ao revisar a sessão.";
            }
            catch
            {
                await _videoStore.Save(clip with { State = SessionVideoState.Interrupted }, CancellationToken.None); throw;
            }
            finally
            {
                _videoStream?.Dispose(); _videoStream = null; _videoClip = null; _recordingCamera = null;
                _videoClock.Text = "";
                if (_recordButton is not null) _recordButton.Text = "Gravar com voz e efeitos";
            }
        }
        finally { _videoGate.Release(); }
    }

    private async Task StopRecordingAfterLeaving()
    {
        try { await StopRecording(); }
        catch (Exception ex) { Debug.WriteLine($"Session recording finalization failed: {ex.GetType().Name}"); }
        finally { DisposeCamera(); }
    }

    private void AddVideoReview(VerticalStackLayout body, ScannerSession session)
    {
        foreach (var clip in _videoClips.Where(x => x.OwnerId == session.OwnerId && x.SessionId == session.Id))
        {
            var panel = new VerticalStackLayout { Spacing = 8, Children = { Text($"GRAVAÇÃO · {clip.CreatedAt.ToLocalTime():dd/MM HH:mm}", 12, Color.FromArgb("#BBA4FF"), true) } };
            if (clip.State == SessionVideoState.Interrupted)
                panel.Children.Add(Text("Esta gravação foi interrompida. As cartas da sessão permanecem salvas.", 14, Color.FromArgb("#FFDA77")));
            else
            {
                panel.Children.Add(Text("Vídeo vertical com sua voz, valores em reais e efeitos. A filmagem original fica preservada no aparelho.", 14, Colors.White));
                panel.Children.Add(Action(File.Exists(_videoStore.OutputPath(clip)) && clip.State == SessionVideoState.Exported ? "Compartilhar vídeo" : "Exportar e compartilhar", async () =>
                {
                    RequireOwner(); var path = _videoStore.OutputPath(clip);
                    if (!File.Exists(path) || clip.State != SessionVideoState.Exported)
                    {
                        _status.Text = "Montando seu vídeo…";
                        await _videoExporter.Export(clip, _videoStore.RawPath(clip), path, new Progress<int>(p => { if (_visible) _status.Text = $"Montando seu vídeo… {p}%"; }), _lifetime.Token);
                        RequireOwner(); var exported = clip with { State = SessionVideoState.Exported }; await _videoStore.Save(exported, _lifetime.Token);
                        _videoClips = _videoClips.Select(x => x.Id == clip.Id ? exported : x).ToArray();
                    }
                    RequireOwner();
                    await Share.Default.RequestAsync(new ShareFileRequest { Title = "Minha abertura na Vaulta", File = new ShareFile(path, "video/mp4") });
                    if (_visible) { ShowReview(); _status.Text = "Seu vídeo está salvo e disponível para compartilhar novamente."; }
                }));
            }
            body.Children.Add(Surface(panel, "#201629"));
        }
    }
}
