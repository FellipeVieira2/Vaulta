using Vaulta.Vision.Contracts;
namespace Vaulta.Vision.Application;
public interface IImageEncoder
{
    EncoderIdentity Identity { get; }
    Task<ImageEmbedding> EncodeAsync(Stream image,CancellationToken ct);
}
public interface IVisualReferenceIndex
{
    VisualIndexStatus Status { get; }
    Task<IReadOnlyList<VisualMatch>> SearchAsync(ImageEmbedding embedding,int topK,CancellationToken ct);
}

public interface IVisualReferenceBuilder
{
    Task<VisualReferenceBuildReport> BuildAsync(Guid? setId,CancellationToken ct);
    Task<VisualIndexStatus> LoadAsync(CancellationToken ct);
}
public sealed record VisualReferenceBuildReport(int Generated,int Unchanged,int Pending,int Failed,VisualIndexStatus Index)
{ public bool Complete=>Pending==0 && Failed==0; }
