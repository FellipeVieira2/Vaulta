using System.Diagnostics;
using CommunityToolkit.Maui.Views;

namespace Vaulta.App.Services.Camera;

public static class CameraPreviewReady
{
    public static async Task Wait(CameraView camera, CancellationToken ct)
    {
        // MAUI Loaded can run before the asynchronous CameraX connection finishes.
        // CameraView.StartVideoRecording silently returns when its native use cases
        // are not ready; require an actual preview stream before enabling capture.
        var clock = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
#if ANDROID
            var preview = camera.Handler?.PlatformView as AndroidX.Camera.View.PreviewView;
            if (preview?.PreviewStreamState.Value?.ToString() == "STREAMING") return;
#else
            if (camera.IsAvailable) return;
#endif
            if (clock.Elapsed >= TimeSpan.FromSeconds(20))
                throw new InvalidOperationException("A câmera não conseguiu iniciar. Volte e abra a sessão novamente ou use a busca pelo nome.");
            await Task.Delay(100, ct);
        }
    }
}
