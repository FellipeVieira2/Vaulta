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
    async Task<VisualSearchSnapshot> SearchSnapshotAsync(ImageEmbedding embedding,int topK,CancellationToken ct)=>new(Status,await SearchAsync(embedding,topK,ct));
}

public interface IVisualReferenceBuilder
{
    Task<VisualReferenceBuildReport> BuildAsync(Guid? setId,CancellationToken ct);
    Task<VisualIndexStatus> LoadAsync(CancellationToken ct);
}
public sealed record VisualReferenceBuildReport(int Generated,int Unchanged,int Pending,int Failed,VisualIndexStatus Index)
{ public bool Complete=>Pending==0 && Failed==0; }

public interface IVisionCatalog
{
    Task<IReadOnlyList<VisionCatalogPrinting>> GetPrintingsAsync(IReadOnlyList<Guid> ids,CancellationToken ct);
    Task<IReadOnlyList<VisionCatalogPrinting>> FindEvidenceCandidatesAsync(Vaulta.Catalog.Application.CardEvidence evidence,CancellationToken ct);
}
public interface IVisionEvidenceReader
{
    Task<VisionEvidenceReading> ReadAsync(byte[] image,IReadOnlyList<VisionCatalogPrinting> candidates,CancellationToken ct);
}
public sealed record VisionEvidenceReading(Vaulta.Catalog.Application.CardEvidence? Evidence,string? Issue,string? OcrText);
public sealed record VisionCaptureInput(byte[] Image,string? FrameId=null);
public sealed record VisionScanInput(IReadOnlyList<VisionCaptureInput> Captures,string? GameCode=null);
