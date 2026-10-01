namespace Vaulta.App.Services.Camera;

public sealed class CameraService : ICameraService
{
    public async Task<byte[]?> CapturePhotoAsync(CancellationToken cancellationToken = default)
    {
        if (!MediaPicker.Default.IsCaptureSupported)
            return null;

        var photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
        {
            Title = "Escanear carta",
            MaximumWidth = 1920,
            MaximumHeight = 1920,
            CompressionQuality = 90,
            RotateImage = true,
            PreserveMetaData = false
        });

        if (photo is null)
            return null;

        using var stream = await photo.OpenReadAsync();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return memoryStream.ToArray();
    }
}
