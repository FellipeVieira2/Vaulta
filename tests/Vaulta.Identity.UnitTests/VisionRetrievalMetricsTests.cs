using Vaulta.Vision.Domain;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public class VisionRetrievalMetricsTests
{
    [Fact] public void PrintingRanksAreDistinctAndFinalAccuracyIsSeparate()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var wrong=Guid.NewGuid();
        var score=VisionRetrievalMetrics.Evaluate([
            new("one",a,[wrong,a,a],wrong,"ambiguous",true,false,false,true),
            new("two",b,[],b,"identified",true,true,false,true),
            new("negative",null,[],null,"not_a_card",false,false,false,false)]);
        Assert.Equal(2,score.Tested);Assert.Equal(0,score.Top1.Correct);Assert.Equal(1,score.Top5.Correct);Assert.Equal(1,score.Top10.Correct);
        Assert.Equal(.25,score.Mrr);Assert.Equal(1,score.FinalPrinting.Correct);Assert.Equal(1,score.Statuses["identified"]);
        Assert.Equal(2,score.NameRead.Correct);Assert.Equal(1,score.CollectorNumberRead.Correct);
    }
    [Fact] public void EmptyBenchmarkDoesNotInventScores()
    {var result=VisionRetrievalMetrics.Evaluate([]);Assert.Null(result.Top1.Accuracy);Assert.Null(result.Mrr);}
}
