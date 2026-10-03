using Vaulta.Vision.Contracts;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Application;
public sealed class CosineReferenceIndex(EncoderIdentity identity,int maxReferences=100000) : IVisualReferenceIndex
{
    public int MaxReferences=>maxReferences;
    private sealed record Snapshot(string Version,VisualIndexEntry[] References);
    private Snapshot _snapshot=new("empty",[]);
    public VisualIndexStatus Status { get { var current=Volatile.Read(ref _snapshot); return new(current.Version,current.References.Length,identity); } }
    public void Publish(EncoderIdentity model,string version,IReadOnlyList<VisualIndexEntry> references)
    {
        if(model!=identity) throw new InvalidOperationException("Index model identity is incompatible with the active encoder.");
        if(references.Count>maxReferences) throw new InvalidOperationException("Visual index exceeds configured capacity; use a partitioned index.");
        if(string.IsNullOrWhiteSpace(version) || references.Select(x=>x.ReferenceId).Distinct().Count()!=references.Count)
            throw new ArgumentException("Invalid index version or duplicate reference identity.");
        // Validate/copy completely before one atomic publication; a failed rebuild keeps the previous snapshot.
        var entries=references.Select(x=>x with { Vector=EmbeddingMath.Normalize(x.Vector,identity.Dimension) }).ToArray();
        Volatile.Write(ref _snapshot,new(version,entries));
    }
    public async Task<IReadOnlyList<VisualMatch>> SearchAsync(ImageEmbedding embedding,int topK,CancellationToken ct)
        =>(await SearchSnapshotAsync(embedding,topK,ct)).Matches;
    public Task<VisualSearchSnapshot> SearchSnapshotAsync(ImageEmbedding embedding,int topK,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(embedding.Identity!=identity) throw new InvalidOperationException("Query embedding is incompatible with the index.");
        if(topK is <1 or >100) throw new ArgumentOutOfRangeException(nameof(topK));
        var vector=EmbeddingMath.Normalize(embedding.Vector,identity.Dimension); var current=Volatile.Read(ref _snapshot);
        var ranked=new List<VisualMatch>(current.References.Length);
        foreach(var reference in current.References)
        {
            ct.ThrowIfCancellationRequested(); double score=0;
            for(var index=0;index<vector.Length;index++) score+=(double)vector[index]*reference.Vector[index];
            ranked.Add(new(reference.ReferenceId,reference.PrintingId,Math.Clamp(score,-1d,1d),reference.Origin));
        }
        return Task.FromResult(new VisualSearchSnapshot(new(current.Version,current.References.Length,identity),ranked.OrderByDescending(x=>x.Similarity).ThenBy(x=>x.ReferenceId).Take(topK).ToArray()));
    }
}
