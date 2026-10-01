using System.Globalization;
using System.Text.Json;
using Android.Media;
using Vaulta.App.Core.Catalog;
using JClass = Java.Lang.Class;

namespace Vaulta.App.Services.Camera;

public sealed class AndroidSessionVideoExporter : ISessionVideoExporter
{
    // Native Transformer and its progress API must run on the same main looper.
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task Export(SessionVideoClip clip, string rawPath, string outputPath, IProgress<int>? progress, CancellationToken ct)
    {
        if (clip.State is not (SessionVideoState.ReadyToExport or SessionVideoState.Exported)) throw new InvalidOperationException("Finalize a gravação antes de exportar.");
        await _gate.WaitAsync(ct);
        var soundPath = outputPath + ".effects.wav"; var temporaryOutput = outputPath + "." + Guid.NewGuid().ToString("N") + ".mp4";
        JClass? bridge = null; bool started = false;
        try
        {
            if (!File.Exists(rawPath)) throw new InvalidOperationException("A filmagem original não está disponível neste aparelho.");
            using var metadata = new MediaMetadataRetriever(); await metadata.SetDataSourceAsync(rawPath);
            if (metadata.ExtractMetadata(MetadataKey.HasAudio) != "yes") throw new InvalidOperationException("A gravação não contém áudio de voz. Grave novamente com o microfone permitido.");
            if (!long.TryParse(metadata.ExtractMetadata(MetadataKey.Duration), CultureInfo.InvariantCulture, out var durationMs) || durationMs <= 0)
                throw new InvalidOperationException("A filmagem foi interrompida e não pode ser exportada.");
            clip = clip.AlignToMediaDuration(durationMs * 1000);
            await using (var sound = new FileStream(soundPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await SessionVideoSoundtrack.Write(sound, clip.Snapshots, durationMs * 1000, ct);
            var context = Android.App.Application.Context;
            bridge = context.ClassLoader!.LoadClass("com.vaulta.video.SessionVideoExporter") ?? throw new InvalidOperationException("Exportador de vídeo indisponível.");
            using var contextClass = JClass.FromType(typeof(Android.Content.Context));
            using var stringClass = JClass.FromType(typeof(Java.Lang.String));
            using var start = bridge.GetMethod("start", contextClass, stringClass, stringClass, stringClass, stringClass);
            using var status = bridge.GetMethod("status");
            using var input = new Java.Lang.String(rawPath); using var audio = new Java.Lang.String(soundPath);
            using var timeline = new Java.Lang.String(JsonSerializer.Serialize(clip)); using var destination = new Java.Lang.String(temporaryOutput);
            ct.ThrowIfCancellationRequested();
            await MainThread.InvokeOnMainThreadAsync(() => { using var result = start!.Invoke(null, context, input, audio, timeline, destination); });
            started = true;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var state = await MainThread.InvokeOnMainThreadAsync(() => { using var result = status!.Invoke(null); return result?.ToString() ?? "failed"; });
                var parts = state.Split('|');
                if (parts[0] == "complete") break;
                if (parts[0] != "running") throw new InvalidOperationException("Não foi possível montar o vídeo. Sua filmagem foi preservada; você pode tentar exportar novamente.");
                if (parts.Length > 1 && int.TryParse(parts[1], out var percent)) progress?.Report(percent);
                await Task.Delay(250, ct);
            }
            ct.ThrowIfCancellationRequested(); File.Move(temporaryOutput, outputPath, overwrite: true); progress?.Report(100);
        }
        finally
        {
            try
            {
                if (started && bridge is not null)
                {
                    try
                    {
                        using var cancel = bridge.GetMethod("cancel");
                        await MainThread.InvokeOnMainThreadAsync(() => { using var result = cancel!.Invoke(null); });
                    }
                    catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"Video export cleanup failed: {ex.GetType().Name}"); }
                }
                bridge?.Dispose();
                foreach (var file in new[] { soundPath, temporaryOutput })
                {
                    try { if (File.Exists(file)) File.Delete(file); }
                    catch (IOException ex) { System.Diagnostics.Debug.WriteLine($"Video temporary cleanup failed: {ex.GetType().Name}"); }
                }
            }
            finally { _gate.Release(); }
        }
    }
}
