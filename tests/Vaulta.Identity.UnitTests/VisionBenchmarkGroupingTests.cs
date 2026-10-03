using Vaulta.Vision.Domain;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public class VisionBenchmarkGroupingTests
{
 [Fact] public void RelatedFramesAndDuplicateHashesStayInTheSameSplitGroup()
 {var first=Guid.NewGuid();var second=Guid.NewGuid();var unrelated=Guid.NewGuid();var groups=VisionBenchmarkGrouping.Assign([(first,"aa"),(first,"bb"),(second,"bb"),(second,"cc"),(unrelated,"dd")]);Assert.Equal(groups[(first,"aa")],groups[(second,"cc")]);Assert.NotEqual(groups[(first,"aa")],groups[(unrelated,"dd")]);}
}
