using System.Net.Http.Json;
using System.Net.Http.Headers;
using Vaulta.App.Core.Http;
using Vaulta.Vision.Contracts;
namespace Vaulta.App.Core.Vision;
public interface IVisionClient
{
 Task<VisionPoliciesDto> PoliciesAsync(CancellationToken ct);
 Task<CreateScanAttemptResponse> CreateAsync(CreateScanAttemptRequest request,CancellationToken ct);
 Task ArchiveAsync(Guid attempt,byte[] image,string sha,CancellationToken ct);
 Task ArchiveFrameAsync(Guid attempt,byte[] image,string sha,int sequence,string role,CancellationToken ct)
     => sequence==0 && role=="card-crop" ? ArchiveAsync(attempt,image,sha,ct) : throw new NotSupportedException("Full-frame archive is unavailable.");
 Task<VisionFeedbackDto> FeedbackAsync(Guid attempt,string key,VisionFeedbackRequest request,CancellationToken ct);
 Task DeleteAsync(Guid attempt,CancellationToken ct);
}
public sealed class VisionClient(HttpClient api):IVisionClient
{
 public async Task<VisionPoliciesDto> PoliciesAsync(CancellationToken ct)
 {using var response=await api.GetAsync("api/v1/vision/policies",ct);return await response.ReadApiJsonAsync<VisionPoliciesDto>(ct);}
 public async Task<CreateScanAttemptResponse> CreateAsync(CreateScanAttemptRequest request,CancellationToken ct)
 {using var response=await api.PostAsJsonAsync("api/v1/vision/attempts",request,ct);return await response.ReadApiJsonAsync<CreateScanAttemptResponse>(ct);}
 public Task ArchiveAsync(Guid attempt,byte[] image,string sha,CancellationToken ct)=>ArchiveFrameAsync(attempt,image,sha,0,"card-crop",ct);
 public async Task ArchiveFrameAsync(Guid attempt,byte[] image,string sha,int sequence,string role,CancellationToken ct)
 {
  var mime=image.AsSpan().StartsWith(new byte[]{137,80,78,71})?"image/png":image.AsSpan().StartsWith("RIFF"u8)?"image/webp":"image/jpeg";
  using var reserve=await api.PostAsJsonAsync($"api/v1/vision/attempts/{attempt}/captures/uploads",new CreateVisionCaptureRequest(mime,image.Length,sha,sequence,role),ct);
  var capture=await reserve.ReadApiJsonAsync<VisionCaptureUploadResponse>(ct);
  // A separate client prevents sending the API Bearer token to object storage.
  using var upload=new HttpClient {Timeout=TimeSpan.FromSeconds(20)};using var body=new ByteArrayContent(image);body.Headers.ContentType=new MediaTypeHeaderValue(mime);
  using var stored=await upload.PutAsync(capture.UploadUrl,body,ct);stored.EnsureSuccessStatusCode();
  using var confirmed=await api.PostAsync($"api/v1/vision/attempts/{attempt}/captures/{capture.CaptureId}/confirm",null,ct);await confirmed.ReadApiJsonAsync<VisionCaptureDto>(ct);
 }
 public async Task<VisionFeedbackDto> FeedbackAsync(Guid attempt,string key,VisionFeedbackRequest request,CancellationToken ct)
 {using var message=new HttpRequestMessage(HttpMethod.Post,$"api/v1/vision/attempts/{attempt}/feedback"){Content=JsonContent.Create(request)};message.Headers.Add("X-Feedback-ID",key);using var response=await api.SendAsync(message,ct);return await response.ReadApiJsonAsync<VisionFeedbackDto>(ct);}
 public async Task DeleteAsync(Guid attempt,CancellationToken ct)
 {using var response=await api.DeleteAsync($"api/v1/vision/attempts/{attempt}",ct);response.EnsureSuccessStatusCode();}
}
