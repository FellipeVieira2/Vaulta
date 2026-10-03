using System.Net;
using System.Text;
using Vaulta.App.Core.Http;
using Xunit;
namespace Vaulta.App.Core.UnitTests.Vision;
public sealed class OwnedArtworkResponseTests
{
 [Theory][InlineData("scanner")][InlineData("collection")][InlineData("marketplace")]
 public async Task NestedReferenceArtworkUsesTheActualApiOrigin(string consumer)
 {
  using var response=new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=new(HttpMethod.Get,"https://api.test/api/v1/"+consumer),Content=new StringContent("{\"printing\":{\"artworkUrl\":\"/api/v1/catalog/printings/123/artwork\"}}",Encoding.UTF8,"application/json")};
  var dto=await response.ReadApiJsonAsync<Envelope>(default);Assert.Equal("https://api.test/api/v1/catalog/printings/123/artwork",dto.Printing.ArtworkUrl);
 }
 public sealed record Envelope(Artwork Printing);public sealed record Artwork(string ArtworkUrl);
}
