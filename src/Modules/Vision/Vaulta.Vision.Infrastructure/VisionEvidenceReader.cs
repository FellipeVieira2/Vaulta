using System.Text.Json;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure.Recognition;
using Vaulta.Vision.Application;
namespace Vaulta.Vision.Infrastructure;
public sealed class VisionEvidenceReader(IOcrService ocr,OpenAiCardEvidenceExtractor gpt) : IVisionEvidenceReader
{
    public async Task<VisionEvidenceReading> ReadAsync(byte[] image,IReadOnlyList<VisionCatalogPrinting> candidates,CancellationToken ct)
    {
        OcrResult? text=null;
        using var ocrBudget=CancellationTokenSource.CreateLinkedTokenSource(ct); ocrBudget.CancelAfter(TimeSpan.FromMilliseconds(1500));
        try { text=await ocr.ExtractTextAsync(image,ocrBudget.Token); }
        catch(OcrUnavailableException) { }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) { }
        // No canonical IDs or prices are passed to the LLM. Context cannot fill invisible fields.
        var context=JsonSerializer.Serialize(new { candidates=candidates.Take(10).Select(x=>new { game=x.Printing.GameCode,name=x.Printing.CardName,set=x.Printing.SetName,number=x.Printing.CollectorNumber,language=x.Printing.Language,hp=x.Hp,stage=x.Stage,variants=x.Variants.Select(v=>new { v.Surface,v.Edition }) }),ocr=text?.RawText is { Length:>4000 }?text.RawText[..4000]:text?.RawText });
        var reading=await gpt.ExtractWithContextAsync(image,context,ct);
        if(reading.Evidence is not null) return new(reading.Evidence,reading.ServiceIssue,text?.RawText);
        // OCR can preserve a conservative partial reading if GPT is unavailable; it does not infer language/game/finish from retrieval.
        CardEvidence? partial=null;
        var name=candidates.Select(x=>x.Printing.CardName).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(x=>text?.RawText.Contains(x,StringComparison.OrdinalIgnoreCase)==true);
        if(name is not null)
        { CardEvidenceField Unknown()=>new(null,0); partial=new(Unknown(),new(name,.7),Unknown(),Unknown(),Unknown(),Unknown(),Unknown(),"ocr-visible-v1","tesseract"); }
        return new(partial,reading.ServiceIssue,text?.RawText);
    }
}
