using Vaulta.App.Core.Vision;
using Vaulta.Vision.Contracts;
using Xunit;
namespace Vaulta.App.Core.UnitTests.Vision;
public class VisionCaptureArchiveTests
{
 [Fact] public async Task OnePendingUploadDropsAnotherWithoutDelayingRecognition()
 {
  var released=new TaskCompletionSource<bool>();var client=new Client(released.Task);var archive=new VisionCaptureArchive(client);
  var first=archive.ArchiveAsync([1,2,3],Guid.NewGuid(),default);Assert.False(await archive.ArchiveAsync([4],Guid.NewGuid(),default));released.SetResult(true);Assert.True(await first);
 }
 [Fact] public async Task StorageFailureReturnsFalseInsteadOfDiscardingScannerResult()
 { var archive=new VisionCaptureArchive(new Client(Task.FromException(new HttpRequestException("storage failed"))));Assert.False(await archive.ArchiveAsync([1,2,3],Guid.NewGuid(),default)); }
 [Fact] public async Task CropAndFullEvidenceFrameKeepTheirExecutedBytesAndRoles()
 {
  var release=new TaskCompletionSource();var client=new FrameClient(release.Task);var archive=new VisionCaptureArchive(client);byte[] crop=[1,2],full=[3,4];
  var storing=archive.ArchiveAsync(crop,full,Guid.NewGuid(),default);crop[0]=9;full[0]=9;release.SetResult();Assert.True(await storing);
  Assert.Collection(client.Frames,x=>{Assert.Equal("card-crop",x.Role);Assert.Equal(0,x.Sequence);Assert.Equal(new byte[]{1,2},x.Image);},x=>{Assert.Equal("full-frame",x.Role);Assert.Equal(1,x.Sequence);Assert.Equal(new byte[]{3,4},x.Image);});
 }
 private sealed class FrameClient(Task wait):IVisionClient
 {
  public List<(string Role,int Sequence,byte[] Image)> Frames=[];
  public async Task ArchiveFrameAsync(Guid attempt,byte[] image,string sha,int sequence,string role,CancellationToken ct)
  {await wait;Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image)).ToLowerInvariant(),sha);Frames.Add((role,sequence,image));}
  public Task ArchiveAsync(Guid attempt,byte[] image,string sha,CancellationToken ct)=>throw new NotSupportedException();
  public Task<VisionPoliciesDto> PoliciesAsync(CancellationToken ct)=>throw new NotSupportedException();public Task<CreateScanAttemptResponse> CreateAsync(CreateScanAttemptRequest r,CancellationToken ct)=>throw new NotSupportedException();
  public Task<VisionFeedbackDto> FeedbackAsync(Guid a,string k,VisionFeedbackRequest r,CancellationToken ct)=>throw new NotSupportedException();public Task DeleteAsync(Guid a,CancellationToken ct)=>throw new NotSupportedException();
 }
 private class Client(Task wait):IVisionClient
 {
  public Task<VisionPoliciesDto> PoliciesAsync(CancellationToken ct)=>throw new NotImplementedException();
  public Task<CreateScanAttemptResponse> CreateAsync(CreateScanAttemptRequest r,CancellationToken ct)=>throw new NotImplementedException();
  public Task<VisionFeedbackDto> FeedbackAsync(Guid attempt,string key,VisionFeedbackRequest r,CancellationToken ct)=>throw new NotImplementedException();
  public Task DeleteAsync(Guid attempt,CancellationToken ct)=>throw new NotImplementedException();
  public async Task ArchiveAsync(Guid attempt,byte[] image,string sha,CancellationToken ct){await wait;Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81",sha);}
 }
}
