using Vaulta.Catalog.Contracts;
namespace Vaulta.Vision.Contracts;
public sealed record VisionFieldDto(string? Value,double Confidence);
public sealed record VisionScanCandidateDto(CatalogPrintingDetails Printing,double? RetrievalScore);
public sealed record VisionScanTraceDto(EncoderIdentity Encoder,string IndexVersion,string ResolverVersion,string? EvidenceModel,string? PromptVersion,long DurationMs,string? RetrievalImageSha256=null,string? EvidenceImageSha256=null,string? RetrievalRole=null);
public sealed record VisionRetrievalMatchDto(Guid PrintingId,Guid ReferenceId,double Similarity,string Origin);
public sealed record VisionScanResultDto(Guid ScanId,string Status,Guid? PrintingId,Guid? VariantId,double PrintingConfidence,double VariantConfidence,
    IReadOnlyList<VisionScanCandidateDto> Candidates,CardMarketQuoteDto? Price,string PriceStatus,string? ReviewReason,
    IReadOnlyDictionary<string,VisionFieldDto> Evidence,VisionScanTraceDto Trace,string? ServiceIssue=null,VisionHistoryTraceDto? History=null,IReadOnlyList<VisionRetrievalMatchDto>? Retrieval=null);
