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
 private class Client(Task wait):IVisionClient
 {
  public Task<VisionPoliciesDto> PoliciesAsync(CancellationToken ct)=>throw new NotImplementedException();
  public Task<CreateScanAttemptResponse> CreateAsync(CreateScanAttemptRequest r,CancellationToken ct)=>throw new NotImplementedException();
  public Task<VisionFeedbackDto> FeedbackAsync(Guid attempt,string key,VisionFeedbackRequest r,CancellationToken ct)=>throw new NotImplementedException();
  public Task DeleteAsync(Guid attempt,CancellationToken ct)=>throw new NotImplementedException();
  public async Task ArchiveAsync(Guid attempt,byte[] image,string sha,CancellationToken ct){await wait;Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81",sha);}
 }
}
