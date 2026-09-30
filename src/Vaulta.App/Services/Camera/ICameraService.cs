namespace Vaulta.App.Services.Camera;

public interface ICameraService
{
    Task<byte[]?> CapturePhotoAsync(CancellationToken cancellationToken = default);
}