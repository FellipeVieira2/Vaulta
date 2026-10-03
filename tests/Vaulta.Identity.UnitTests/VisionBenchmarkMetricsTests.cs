using Vaulta.Vision.Domain;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public class VisionBenchmarkMetricsTests
{
 [Fact] public void UnknownFinishIsExcludedWithoutInflatingOtherTaskMetrics()
 {var a=Guid.NewGuid();var variant=Guid.NewGuid();var result=VisionBenchmarkMetrics.Evaluate([new("a",a,null,"front","card-present"),new("b",null,null,"back","card-present"),new("c",a,variant,"front","card-present")],[new("a",a,null,"front","card-present"),new("b",null,null,"unknown","card-present"),new("c",Guid.NewGuid(),variant,"front","no-card")]);Assert.Equal(2,result.Printing.Tested);Assert.Equal(1,result.Printing.Correct);Assert.Equal(1,result.Variant.Tested);Assert.Equal(0,result.Variant.Correct);Assert.Equal(2,result.Orientation.Correct);Assert.Equal(2,result.Presence.Correct);}
 [Fact] public void EmptyMetricHasNoInventedAccuracy()
 {var result=VisionBenchmarkMetrics.Evaluate([],[]);Assert.Null(result.Variant.Accuracy);}
}
