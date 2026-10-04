using System.Security.Cryptography;
namespace Vaulta.App.Core.Vision;
public sealed class VisionCaptureArchive(IVisionClient client)
{
 private readonly SemaphoreSlim _slot=new(1,1);
 public Task<bool> ArchiveAsync(byte[] image,Guid attempt,CancellationToken ct)=>ArchiveAsync(image,null,attempt,ct);
 public async Task<bool> ArchiveAsync(byte[] image,byte[]? fullFrame,Guid attempt,CancellationToken ct)
 {
  if(image.Length is 0 or >15_000_000 || !await _slot.WaitAsync(0,ct)) return false;
  try
  {
   var immutable=image.ToArray();var evidence=fullFrame?.ToArray();
   var sha=Convert.ToHexString(SHA256.HashData(immutable)).ToLowerInvariant();await client.ArchiveFrameAsync(attempt,immutable,sha,0,"card-crop",ct);
   if(evidence is {Length:>0 and <=15_000_000} && !evidence.AsSpan().SequenceEqual(immutable))
   {var evidenceSha=Convert.ToHexString(SHA256.HashData(evidence)).ToLowerInvariant();await client.ArchiveFrameAsync(attempt,evidence,evidenceSha,1,"full-frame",ct);}
   return true;
  }
  catch(Exception e) when(e is HttpRequestException or OperationCanceledException or Vaulta.App.Core.Http.ApiException){return false;}
  finally {_slot.Release();}
 }
}
