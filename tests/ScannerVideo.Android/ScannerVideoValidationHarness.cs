using System.Diagnostics;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Services.Camera;

namespace Vaulta.App;

// Runs only in an explicitly enabled Debug build. Uses synthetic session values and
// the emulator camera; it does not sign in, call providers or change a collection.
internal static class ScannerVideoValidationHarness
{
    public static async void Start()
    {
        var directory = Path.Combine(FileSystem.AppDataDirectory, "video-validation");
        Directory.CreateDirectory(directory);
        var log = Path.Combine(directory, "result.txt");
        try
        {
            await File.WriteAllTextAsync(log, "Starting camera validation\n");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var ct = timeout.Token;
            var camera = new CameraView { ImageCaptureResolution = new Size(1920, 1080) };
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            camera.Loaded += (_, _) => loaded.TrySetResult();
            var page = new ContentPage { Content = camera };
            var root = Microsoft.Maui.Controls.Application.Current!.Windows[0].Page!;
            await root.Navigation.PushModalAsync(page);
            await loaded.Task.WaitAsync(ct);
            var cameras = await camera.GetAvailableCameras(ct);
            camera.SelectedCamera = cameras.FirstOrDefault(x => x.Position == CameraPosition.Rear)
                ?? cameras.FirstOrDefault() ?? throw new InvalidOperationException("No camera");
            await camera.StartCameraPreview(ct);
            await CameraPreviewReady.Wait(camera, ct);
            var signature = CameraSceneSampler.Read(camera);
            if (signature is null || signature.Length != 288) throw new InvalidOperationException("Continuous camera sampling failed");
            await File.AppendAllTextAsync(log, $"Continuous preview sample: {signature.Length} bytes\n", ct);
            var rawPath = Path.Combine(directory, "camera.raw.mp4");
            await using (var raw = new FileStream(rawPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            {
                await camera.StartVideoRecording(raw, ct);
                var clock = Stopwatch.StartNew();
                await Task.Delay(1000, ct);
                // Verify image capture while CameraX is also recording.
                await using var image = await camera.CaptureImage(ct);
                if (image.Length == 0) throw new InvalidOperationException("Empty image during recording");
                await File.AppendAllTextAsync(log, $"Image capture during recording: {image.Length} bytes\n", ct);
                var first = clock.Elapsed.Ticks / 10;
                await Task.Delay(1800, ct);
                var second = clock.Elapsed.Ticks / 10;
                await Task.Delay(2400, ct);
                clock.Stop();
                await camera.StopVideoRecording(ct);
                await raw.FlushAsync(ct);
                var clip = new SessionVideoClip(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
                    SessionVideoState.ReadyToExport, true,
                    [new(0, 0, 100, 0, 0, null, null, false, false),
                     new(first, 25, 100, 1, 0, "Carta de teste", 25, false, true),
                     new(second, 275, 100, 2, 0, "Grande achado de teste", 250, true, true)], CaptureDurationUs: clock.Elapsed.Ticks / 10);
                await File.WriteAllTextAsync(Path.Combine(directory, "timeline.json"), System.Text.Json.JsonSerializer.Serialize(clip), ct);
                camera.StopCameraPreview(); camera.Dispose();
                var output = Path.Combine(directory, "session.share.mp4");
                await new AndroidSessionVideoExporter().Export(clip, rawPath, output,
                    new Progress<int>(p => Android.Util.Log.Info("VaultaVideoValidation", $"Export {p}%")), ct);
                using var metadata = new Android.Media.MediaMetadataRetriever();
                await metadata.SetDataSourceAsync(output);
                var aligned = clip.AlignToMediaDuration(long.Parse(metadata.ExtractMetadata(Android.Media.MetadataKey.Duration)!) * 1000);
                await File.AppendAllTextAsync(log, $"PASS: export {new FileInfo(output).Length} bytes\n" +
                    $"Width={metadata.ExtractMetadata(Android.Media.MetadataKey.VideoWidth)}\n" +
                    $"Height={metadata.ExtractMetadata(Android.Media.MetadataKey.VideoHeight)}\n" +
                    $"Rotation={metadata.ExtractMetadata(Android.Media.MetadataKey.VideoRotation)}\n" +
                    $"Audio={metadata.ExtractMetadata(Android.Media.MetadataKey.HasAudio)}\n" +
                    $"DurationMs={metadata.ExtractMetadata(Android.Media.MetadataKey.Duration)}\n", ct);
                foreach (var frameTime in new[] { 0L, aligned.Snapshots[1].PositionUs + 800000, aligned.Snapshots[2].PositionUs + 800000 })
                {
                    using var frame = metadata.GetFrameAtTime(frameTime, Android.Media.Option.Closest);
                    if (frame is null) throw new InvalidOperationException("Cannot decode exported frame");
                    await using var file = File.Create(Path.Combine(directory, $"frame-{frameTime}.png"));
                    await frame.CompressAsync(Android.Graphics.Bitmap.CompressFormat.Png!, 100, file);
                }
            }
            Android.Util.Log.Info("VaultaVideoValidation", "PASS");
        }
        catch (Exception ex)
        {
            await File.AppendAllTextAsync(log, "FAIL: " + ex + "\n");
            Android.Util.Log.Error("VaultaVideoValidation", ex.ToString());
        }
    }
}
