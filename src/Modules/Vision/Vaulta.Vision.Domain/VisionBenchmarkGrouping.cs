namespace Vaulta.Vision.Domain;
public static class VisionBenchmarkGrouping
{
 public static IReadOnlyDictionary<(Guid Attempt,string Sha),string> Assign(IReadOnlyList<(Guid Attempt,string Sha)> samples)
 {
  var parent=new Dictionary<string,string>(StringComparer.Ordinal);
  string Root(string value){if(!parent.ContainsKey(value))parent[value]=value;if(parent[value]!=value)parent[value]=Root(parent[value]);return parent[value];}
  void Union(string left,string right){left=Root(left);right=Root(right);if(string.CompareOrdinal(left,right)<=0)parent[right]=left;else parent[left]=right;}
  foreach(var group in samples.GroupBy(x=>x.Attempt)){var hashes=group.Select(x=>x.Sha).Distinct().ToArray();foreach(var sha in hashes)Union(hashes[0],sha);}
  return samples.Distinct().ToDictionary(x=>x,x=>Root(x.Sha));
 }
}
