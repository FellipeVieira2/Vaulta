using System.Security.Cryptography;
namespace Vaulta.App.Core.Vision;
public sealed class VisionCaptureArchive(IVisionClient client)
{
 private readonly SemaphoreSlim _slot=new(1,1);
 public async Task<bool> ArchiveAsync(byte[] image,Guid attempt,CancellationToken ct)
 {
  if(image.Length is 0 or >15_000_000 || !await _slot.WaitAsync(0,ct)) return false;
  try {var immutable=image.ToArray();var sha=Convert.ToHexString(SHA256.HashData(immutable)).ToLowerInvariant();await client.ArchiveAsync(attempt,immutable,sha,ct);return true;}
  catch(Exception e) when(e is HttpRequestException or OperationCanceledException or Vaulta.App.Core.Http.ApiException){return false;}
  finally {_slot.Release();}
 }
}
