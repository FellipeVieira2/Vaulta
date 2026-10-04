namespace Vaulta.Vision.Domain;

public sealed record VisionRetrievalObservation(string Sample,Guid? ExpectedPrinting,IReadOnlyList<Guid> RankedPrintings,Guid? FinalPrinting,string Status,
    bool NameRead,bool CollectorNumberRead,bool SetRead,bool LanguageRead,bool? EvidenceCompatible=null);
public sealed record VisionRetrievalScore(int Tested,VisionTaskMetric Top1,VisionTaskMetric Top5,VisionTaskMetric Top10,double? Mrr,
    VisionTaskMetric FinalPrinting,VisionTaskMetric NameRead,VisionTaskMetric CollectorNumberRead,VisionTaskMetric SetRead,VisionTaskMetric LanguageRead,
    IReadOnlyDictionary<string,int> Statuses,IReadOnlyDictionary<string,int> FailureAttribution);
public static class VisionRetrievalMetrics
{
    public static int? Rank(Guid? expected,IEnumerable<Guid> ranked)
    {
        if(expected is null)return null;
        var ids=ranked.Distinct().ToArray();var at=Array.IndexOf(ids,expected.Value);return at<0?null:at+1;
    }
    public static string FailureSource(VisionRetrievalObservation x)
    {
        if(x.ExpectedPrinting is null || x.FinalPrinting==x.ExpectedPrinting)return "none";
        if(Rank(x.ExpectedPrinting,x.RankedPrintings) is not <=10)return "retrieval";
        return x.EvidenceCompatible switch {false=>"evidence",true=>"resolver",_=>"unknown"};
    }
    public static VisionRetrievalScore Evaluate(IReadOnlyList<VisionRetrievalObservation> samples)
    {
        var labeled=samples.Where(x=>x.ExpectedPrinting is not null).ToArray();
        var ranks=labeled.Select(x=>Rank(x.ExpectedPrinting,x.RankedPrintings)).ToArray();
        VisionTaskMetric Top(int k)=>new(labeled.Length,ranks.Count(x=>x is >0 && x<=k));
        VisionTaskMetric Read(Func<VisionRetrievalObservation,bool> value)=>new(labeled.Length,labeled.Count(value));
        return new(labeled.Length,Top(1),Top(5),Top(10),labeled.Length==0?null:ranks.Sum(r=>r is >0?1d/r.Value:0)/labeled.Length,
            Read(x=>x.FinalPrinting==x.ExpectedPrinting),Read(x=>x.NameRead),Read(x=>x.CollectorNumberRead),Read(x=>x.SetRead),Read(x=>x.LanguageRead),
            samples.GroupBy(x=>x.Status).ToDictionary(g=>g.Key,g=>g.Count()),
            labeled.Select(FailureSource).Where(x=>x!="none").GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count()));
    }
}
