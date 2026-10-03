using Vaulta.Catalog.Contracts;

using Vaulta.Vision.Contracts;

namespace Vaulta.App.Core.Catalog;

public static class VisionAcceptance

{

    public static ScannerSessionCard? CreateCard(VisionScanResultDto result,bool confirmed=false,Guid? selectedPrinting=null,Guid? selectedVariant=null)

    {

        if(result.ScanId==Guid.Empty || !confirmed && (result.Status!="identified" || !double.IsFinite(result.PrintingConfidence) || result.PrintingConfidence is <.8 or >1)) return null;

        var id=selectedPrinting??result.PrintingId;

        var printing=result.Candidates.FirstOrDefault(x=>x.Printing.PrintingId==id)?.Printing;

        if(printing is null || printing.PrintingId==Guid.Empty) return null;

        var variantId=selectedVariant??result.VariantId;

        var variant=printing.Variants.FirstOrDefault(x=>x.Id==variantId);

        SessionMarketValue? value=null;

        if(result.PriceStatus=="available" && result.PrintingId==printing.PrintingId && result.Price is { } price && variant is not null && price.VariantId==variant.Id

            && double.IsFinite(result.VariantConfidence) && result.VariantConfidence is >=.8 and <=1 && price.MarketValueBrl>=0 && price.UpdatedAt!=default && !string.IsNullOrWhiteSpace(price.Source))

            value=new(decimal.Round(price.MarketValueBrl,2),price.Source,price.UpdatedAt,price.OriginalValue,price.OriginalCurrency,price.ExchangeRate,price.ExchangeRateAt);

        return new(result.ScanId,printing.PrintingId,variant?.Id,printing.CardName,printing.SetName,printing.CollectorNumber,variant?.Name??"Acabamento pendente","UNKNOWN",printing.ArtworkUrl,value,DateTimeOffset.UtcNow,VisualIdentification:Visual(result,printing),History:result.History);

    }

    private static CardVisualIdentificationDto Visual(VisionScanResultDto result,CatalogPrintingDetails printing)
    {
        string? Read(string key)=>result.Evidence.TryGetValue(key,out var field) && double.IsFinite(field.Confidence) && field.Confidence is >=.8 and <=1 ? field.Value:null;
        int? Number(string key)=>int.TryParse(Read(key),out var value)?value:null;
        var certification=Read("isGraded")=="true" ? new CardCertificationDto(Read("gradingCompany"),Read("grade"),Read("certificationNumber")):null;
        return new(printing.CardName,printing.CollectorNumber,printing.Language,printing.SetName,result.PrintingConfidence,printing.GameCode,Number("hp"),Read("finish"),null,certification,new(Read("rarity"),Number("year"),Read("cardType"),Read("stage")));
    }
    // The older manual detail screen only displays canonical candidates, without pricing guesses.

    public static CardScanResultDto ManualCandidates(VisionScanResultDto result)=>new(result.Candidates.Select(x=>new CardScanCandidateDto(x.Printing.PrintingId,x.Printing.CardName,x.Printing.SetName,x.Printing.CollectorNumber,x.Printing.Rarity,x.Printing.ArtworkUrl,null,null,x.Printing.Variants.Select(v=>v.Code).ToArray(),0)).ToArray(),ServiceIssue:result.ServiceIssue);

}

