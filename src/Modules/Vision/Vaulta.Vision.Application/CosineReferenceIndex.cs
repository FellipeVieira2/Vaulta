using Vaulta.Vision.Contracts;
using Vaulta.Vision.Domain;
namespace Vaulta.Vision.Application;
// Compact immutable retrieval snapshots; full-precision embeddings remain persisted in PostgreSQL.
public sealed class CosineReferenceIndex(EncoderIdentity identity,int maxReferences=250000) : IVisualReferenceIndex
{
    private readonly EncoderIdentity _identity=identity;
    public int MaxReferences=>maxReferences;
    public const string StorageVersion="cosine-half-printing-v2";
    private sealed record Reference(Guid ReferenceId,Guid PrintingId,Half[] Vector,double InverseNorm,string Origin);
    private sealed record Snapshot(string Version,Reference[] References);
    private Snapshot _snapshot=new("empty",[]);
    public VisualIndexStatus Status { get { var current=Volatile.Read(ref _snapshot); return new(current.Version,current.References.Length,_identity); } }
    public sealed class BuildSession
    {
        private readonly CosineReferenceIndex _owner;
        private readonly List<Reference> _entries=[];
        private readonly HashSet<Guid> _ids=[];
        private bool _published;
        private bool _failed;
        internal BuildSession(CosineReferenceIndex owner)=>_owner=owner;
        public int Count=>_entries.Count;
        public void Add(VisualIndexEntry entry)
        {
            try
            {
                if(_published || _failed) throw new InvalidOperationException("Snapshot is already published.");
                if(Count>=_owner.MaxReferences) throw new InvalidOperationException("Visual index exceeds configured capacity.");
                if(_ids.Contains(entry.ReferenceId)) throw new ArgumentException("Duplicate reference identity.");
                var normalized=EmbeddingMath.Normalize(entry.Vector,_owner._identity.Dimension);
                var vector=new Half[normalized.Length];double norm=0;
                for(var i=0;i<vector.Length;i++) { vector[i]=(Half)normalized[i];norm+=(double)(float)vector[i]*(float)vector[i]; }
                _entries.Add(new(entry.ReferenceId,entry.PrintingId,vector,1/Math.Sqrt(norm),entry.Origin));
                _ids.Add(entry.ReferenceId);
            }
            catch { _failed=true; throw; }
        }
        public VisualIndexStatus Publish(string version)
        {
            if(_published || _failed) throw new InvalidOperationException("Snapshot is already published or invalid.");
            if(string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Invalid index version.");
            Volatile.Write(ref _owner._snapshot,new(version,_entries.ToArray()));
            _published=true;return _owner.Status;
        }
    }
    // Staging can consume database pages without retaining their float[] vectors.
    public BuildSession BeginSnapshot(EncoderIdentity model)
    {
        if(model!=_identity) throw new InvalidOperationException("Index model identity is incompatible with the active encoder.");
        return new(this);
    }
    public void Publish(EncoderIdentity model,string version,IReadOnlyList<VisualIndexEntry> references)
    {
        if(references.Count>maxReferences) throw new InvalidOperationException("Visual index exceeds configured capacity.");
        var session=BeginSnapshot(model);foreach(var entry in references)session.Add(entry);session.Publish(version);
    }
    public async Task<IReadOnlyList<VisualMatch>> SearchAsync(ImageEmbedding embedding,int topK,CancellationToken ct)
        =>(await SearchSnapshotAsync(embedding,topK,ct)).Matches;
    public Task<VisualSearchSnapshot> SearchSnapshotAsync(ImageEmbedding embedding,int topK,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(embedding.Identity!=_identity) throw new InvalidOperationException("Query embedding is incompatible with the index.");
        if(topK is <1 or >100) throw new ArgumentOutOfRangeException(nameof(topK));
        var vector=EmbeddingMath.Normalize(embedding.Vector,_identity.Dimension);var current=Volatile.Read(ref _snapshot);
        var comparer=Comparer<(double Score,Guid Id)>.Create((a,b)=>a.Score!=b.Score ? a.Score.CompareTo(b.Score) : b.Id.CompareTo(a.Id));
        var ranked=new PriorityQueue<VisualMatch,(double Score,Guid Id)>(comparer);
        // Bound the working set by topK. Multiple confirmed photos improve a
        // printing's best score without occupying slots belonging to other cards.
        var selected=new Dictionary<Guid,VisualMatch>();
        foreach(var reference in current.References)
        {
            ct.ThrowIfCancellationRequested();double score=0;
            for(var i=0;i<vector.Length;i++)score+=(double)vector[i]*(float)reference.Vector[i];
            score=Math.Clamp(score*reference.InverseNorm,-1d,1d);
            if(selected.TryGetValue(reference.PrintingId,out var prior))
            {
                if(score<prior.Similarity || score==prior.Similarity && reference.ReferenceId.CompareTo(prior.ReferenceId)>=0)continue;
                ranked.Remove(prior,out _,out _);
                selected.Remove(reference.PrintingId);
            }
            if(ranked.Count==topK && ranked.TryPeek(out _,out var worst))
            {
                if(score<worst.Score || score==worst.Score && reference.ReferenceId.CompareTo(worst.Id)>=0)continue;
                var removed=ranked.Dequeue();selected.Remove(removed.PrintingId);
            }
            var match=new VisualMatch(reference.ReferenceId,reference.PrintingId,score,reference.Origin);
            selected.Add(reference.PrintingId,match);ranked.Enqueue(match,(score,reference.ReferenceId));
        }
        return Task.FromResult(new VisualSearchSnapshot(new(current.Version,current.References.Length,_identity),
            ranked.UnorderedItems.Select(x=>x.Element).OrderByDescending(x=>x.Similarity).ThenBy(x=>x.ReferenceId).ToArray()));
    }
}
