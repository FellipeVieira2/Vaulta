using System.Security.Cryptography;
using System.Diagnostics;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Vision.Contracts;
namespace Vaulta.Vision.Application;
public sealed class VisionEvidenceCandidateLimitException() : InvalidOperationException("Evidence candidate limit exceeded.");
public sealed class VisionBusyException() : Exception("O scanner está ocupado. Tente novamente em instantes.");
public sealed class VisionScanCapacity
{
    internal static VisionScanCapacity Shared { get; }=new();
    internal SemaphoreSlim Slots { get; }=new(4,4);
}
public sealed class VisionScannerService(IImageEncoder encoder,IVisualReferenceIndex index,IVisionCatalog catalog,IVisionEvidenceReader evidence,
    IScannerCardDetailsReader quotes,VisionPrintingResolver resolver,VisionScanCapacity? capacity=null)
{
    public async Task<VisionScanResultDto> IdentifyAsync(VisionScanInput input,CancellationToken ct)=>(await ExecuteAsync(input,ct)).Result;
    public async Task<VisionScanExecution> ExecuteAsync(VisionScanInput input,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if(input.Captures.Count is <1 or >2 || input.Captures.Any(x=>x.Image.Length is 0 or >15*1024*1024)) throw new ArgumentException("Envie capturas de até 15 MB.");
        if(input.Captures.Count==2 && (input.Captures.Count(x=>x.Role=="card-crop")!=1 || input.Captures.Count(x=>x.Role=="full-frame")!=1)) throw new ArgumentException("Informe um recorte e uma foto completa.");
        var retrievalCapture=input.Captures.FirstOrDefault(x=>x.Role=="card-crop")??input.Captures[0];
        var evidenceCapture=input.Captures.FirstOrDefault(x=>x.Role=="full-frame")??input.Captures[0];
        var slots=(capacity??VisionScanCapacity.Shared).Slots;
        if(!await slots.WaitAsync(0,ct)) throw new VisionBusyException();
        try
        {
            var timer=Stopwatch.StartNew(); var image=retrievalCapture.Image;
            using var stream=new MemoryStream(image,false); var encoded=await encoder.EncodeAsync(stream,ct);
            var retrieval=await index.SearchSnapshotAsync(encoded,10,ct);
            var candidates=await catalog.GetPrintingsAsync(retrieval.Matches.Select(x=>x.PrintingId).Distinct().ToArray(),ct);
            var reading=await evidence.ReadAsync(evidenceCapture.Image,candidates,ct); var observed=reading.Evidence;
            VisionResolution resolution;
            if(VisionPrintingResolver.Usable(observed?.IsCard) && observed!.IsCard!.Value=="false") resolution=new("not_a_card",null,0,null,0,null);
            else if(VisionPrintingResolver.Usable(observed?.CardSide) && observed!.CardSide!.Value=="back") resolution=new("back",null,0,null,0,"turn_card");
            else
            {
                // Search the whole local name group too: a reprint missing from Top-K must not disappear from ambiguity checks.
                var overflow=false;
                if(observed is not null && VisionPrintingResolver.Usable(observed.Name,.6))
                    try { candidates=candidates.Concat(await catalog.FindEvidenceCandidatesAsync(observed,ct)).DistinctBy(x=>x.Printing.PrintingId).ToArray(); }
                    catch(VisionEvidenceCandidateLimitException) { overflow=true; }
                resolution=overflow?new("ambiguous",null,0,null,0,"candidate_limit"):resolver.Resolve(candidates,observed);
            }
            CardMarketQuoteDto? quote=null;
            var graded=VisionPrintingResolver.Usable(observed?.IsGraded) && observed!.IsGraded!.Value=="true";
            if(!graded && resolution.PrintingId is { } printing && resolution.VariantId is { } variant)
            { var details=await quotes.GetAsync(printing,ct); quote=details?.MarketQuotes.SingleOrDefault(x=>x.VariantId==variant); }
            var fields=new Dictionary<string,VisionFieldDto>();
            if(observed is not null)
                foreach(var property in typeof(CardEvidence).GetProperties())
                    if(property.GetValue(observed) is CardEvidenceField field) fields[char.ToLowerInvariant(property.Name[0])+property.Name[1..]]=new(field.Value,field.Confidence);
            var scores=retrieval.Matches.GroupBy(x=>x.PrintingId).ToDictionary(x=>x.Key,x=>x.Max(x=>x.Similarity));
            var displayed=candidates.OrderByDescending(x=>x.Printing.PrintingId==resolution.PrintingId).ThenByDescending(x=>scores.GetValueOrDefault(x.Printing.PrintingId,-1)).Take(5)
                .Select(x=>new VisionScanCandidateDto(x.Printing,scores.TryGetValue(x.Printing.PrintingId,out var score)?score:null)).ToArray();
            var result=new VisionScanResultDto(Guid.NewGuid(),resolution.Status,resolution.PrintingId,resolution.VariantId,resolution.PrintingConfidence,resolution.VariantConfidence,displayed,quote,
                quote is not null?"available":graded?"graded_unavailable":resolution.PrintingId is null?"unresolved":resolution.VariantId is null?"variant_pending":"unavailable",resolution.ReviewReason,fields,
                new(encoded.Identity,retrieval.Index.Version,VisionPrintingResolver.Version,observed?.ModelVersion,observed?.PromptVersion,timer.ElapsedMilliseconds,Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant(),Convert.ToHexString(SHA256.HashData(evidenceCapture.Image)).ToLowerInvariant(),retrievalCapture.Role??"full-frame"),reading.Issue);
            result=result with {Retrieval=retrieval.Matches.Select(x=>new VisionRetrievalMatchDto(x.PrintingId,x.ReferenceId,x.Similarity,x.Origin)).ToArray()};
            return new(result,encoded);
        }
        finally { slots.Release(); }
    }
}

public sealed record VisionScanExecution(VisionScanResultDto Result,ImageEmbedding Embedding);
