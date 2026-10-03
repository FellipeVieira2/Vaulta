namespace Vaulta.Vision.Domain;
public sealed record VisionBenchmarkLabel(string Sample,Guid? Printing,Guid? Variant,string? Orientation,string? Presence);
public sealed record VisionTaskMetric(int Tested,int Correct){public double? Accuracy=>Tested==0?null:Correct/(double)Tested;}
public sealed record VisionBenchmarkScore(VisionTaskMetric Printing,VisionTaskMetric Variant,VisionTaskMetric Orientation,VisionTaskMetric Presence);
public static class VisionBenchmarkMetrics
{
 public static VisionBenchmarkScore Evaluate(IReadOnlyList<VisionBenchmarkLabel> expected,IReadOnlyList<VisionBenchmarkLabel> actual)
 {
  var predictions=actual.ToDictionary(x=>x.Sample);var printingTested=0;var printingCorrect=0;var variantTested=0;var variantCorrect=0;var orientationTested=0;var orientationCorrect=0;var presenceTested=0;var presenceCorrect=0;
  foreach(var label in expected)
  {
   predictions.TryGetValue(label.Sample,out var predicted);
   if(label.Printing is not null){printingTested++;if(predicted?.Printing==label.Printing)printingCorrect++;}
   if(label.Variant is not null){variantTested++;if(predicted?.Printing==label.Printing && predicted?.Variant==label.Variant)variantCorrect++;}
   if(label.Orientation is not null){orientationTested++;if(predicted?.Orientation==label.Orientation)orientationCorrect++;}
   if(label.Presence is not null){presenceTested++;if(predicted?.Presence==label.Presence)presenceCorrect++;}
  }
  return new(new(printingTested,printingCorrect),new(variantTested,variantCorrect),new(orientationTested,orientationCorrect),new(presenceTested,presenceCorrect));
 }
}
