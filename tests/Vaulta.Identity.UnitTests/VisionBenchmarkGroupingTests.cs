using Vaulta.Vision.Domain;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public class VisionBenchmarkGroupingTests
{
 [Fact] public void RelatedFramesAndDuplicateHashesStayInTheSameSplitGroup()
 {var first=Guid.NewGuid();var second=Guid.NewGuid();var unrelated=Guid.NewGuid();var groups=VisionBenchmarkGrouping.Assign([(first,"aa"),(first,"bb"),(second,"bb"),(second,"cc"),(unrelated,"dd")]);Assert.Equal(groups[(first,"aa")],groups[(second,"cc")]);Assert.NotEqual(groups[(first,"aa")],groups[(unrelated,"dd")]);}
 [Fact] public void DistinctFramesFromTheSameSessionStayInOneSplitGroup()
 {
  var a=Guid.NewGuid();var b=Guid.NewGuid();var session=Guid.NewGuid();IReadOnlyList<(Guid Attempt,string Sha)> frames=[(a,"first"),(b,"second")];IReadOnlyDictionary<Guid,Guid?> sessions=new Dictionary<Guid,Guid?>{{a,session},{b,session}};
  var method=typeof(Vaulta.Vision.Domain.VisionBenchmarkGrouping).GetMethod("Assign",[typeof(IReadOnlyList<(Guid Attempt,string Sha)>),typeof(IReadOnlyDictionary<Guid,Guid?>)]);Assert.NotNull(method);
  var groups=(IReadOnlyDictionary<(Guid Attempt,string Sha),string>)method.Invoke(null,[frames,sessions])!;Assert.Equal(groups[(a,"first")],groups[(b,"second")]);
 }

}
