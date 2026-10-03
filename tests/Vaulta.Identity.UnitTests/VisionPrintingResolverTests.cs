using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Vision.Application;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public sealed class VisionPrintingResolverTests
{
    [Fact] public void CollectorNumberAndLanguageRejectSameArtworkWrongPrinting()
    {
        var correct=Printing("067/086","pt-BR"); var wrongNumber=Printing("062/086","pt-BR"); var wrongLanguage=Printing("067/086","en");
        var result=new VisionPrintingResolver().Resolve([wrongNumber,wrongLanguage,correct],Evidence("067/086"));
        Assert.Equal("identified",result.Status); Assert.Equal(correct.Printing.PrintingId,result.PrintingId);
    }
    [Fact] public void MissingSmallNumberCanResolveUniqueNameHpAndLanguage()
    {
        var card=Printing("067/086","pt-BR"); var result=new VisionPrintingResolver().Resolve([card],Evidence(null));
        Assert.Equal("identified",result.Status); Assert.Equal(card.Printing.PrintingId,result.PrintingId);
    }
    [Fact] public void MissingNumberCannotBreakSameArtworkReprintTie()
    {
        var result=new VisionPrintingResolver().Resolve([Printing("067/086","pt-BR"),Printing("015/100","pt-BR")],Evidence(null));
        Assert.Equal("ambiguous",result.Status); Assert.Null(result.PrintingId);
    }
    [Fact] public void RetrievalAloneDoesNotBecomeConfidence()
    {
        var result=new VisionPrintingResolver().Resolve([Printing("067/086","pt-BR")],null);
        Assert.NotEqual("identified",result.Status); Assert.Null(result.PrintingId);
    }
    [Theory] [InlineData(.8,"identified")] [InlineData(.79,"partial")]
    public void EightyPercentPolicyUsesEvidenceConfidence(double confidence,string status)
    {
        var result=new VisionPrintingResolver().Resolve([Printing("067/086","pt-BR")],Evidence("067/086",confidence)); Assert.Equal(status,result.Status);
    }
    [Fact] public void UnknownFinishDoesNotSelectAPriceVariant()
    {
        var result=new VisionPrintingResolver().Resolve([Printing("067/086","pt-BR")],Evidence("067/086") with { Variant=new(null,0) });
        Assert.Equal("identified",result.Status); Assert.Null(result.VariantId); Assert.True(result.VariantConfidence<.8);
    }
    [Fact] public void KnownNormalFinishSelectsOnlyCompatibleVariant()
    {
        var card=Printing("067/086","pt-BR"); var result=new VisionPrintingResolver().Resolve([card],Evidence("067/086"));
        Assert.Equal(card.Variants[0].Id,result.VariantId); Assert.True(result.VariantConfidence>=.8);
    }
    [Fact] public void ConflictingHighConfidenceHpRejectsWrongCard()
    {
        var result=new VisionPrintingResolver().Resolve([Printing("067/086","pt-BR")],Evidence("067/086") with { Hp=new("90",.96) });
        Assert.Equal("not_in_catalog",result.Status); Assert.Null(result.PrintingId);
    }
    [Fact] public void NonFiniteConfidenceCannotAuthorizePrinting()
    {
        var result=new VisionPrintingResolver().Resolve([Printing("067/086","pt-BR")],Evidence("067/086") with { GameCode=new("pokemon",double.NaN) });
        Assert.NotEqual("identified",result.Status);
    }
    [Fact] public void ContradictoryFinishDoesNotAuthorizeVariant()
    {
        var result=new VisionPrintingResolver().Resolve([Printing("067/086","pt-BR")],Evidence("067/086") with { Finish=new("reverse",.96) });
        Assert.Null(result.VariantId);
    }
    [Fact] public void EditionEvidenceDistinguishesSameSurfaceVariants()
    {
        var card=Printing("067/086","pt-BR"); var first=Guid.NewGuid(); var unlimited=Guid.NewGuid();
        card=card with { Variants=[new(first,"normal-first-edition","First edition","normal","first-edition"),new(unlimited,"normal-unlimited","Unlimited","normal","unlimited")] };
        var result=new VisionPrintingResolver().Resolve([card],Evidence("067/086") with { Edition=new("first-edition",.96) });
        Assert.Equal(first,result.VariantId);
        var unknown=new VisionPrintingResolver().Resolve([card],Evidence("067/086")); Assert.Null(unknown.VariantId);
    }
    public static CardEvidence Evidence(string? number,double confidence=.96)
    { CardEvidenceField F(string? s)=>new(s,s is null?0:confidence); return new(F("pokemon"),F("Golisopod"),F(number),F(null),F(null),F("pt-BR"),F("normal"),"fixture","fixture",F("140")); }
    public static VisionCatalogPrinting Printing(string number,string language)
    {
        var id=Guid.NewGuid(); var normal=Guid.NewGuid(); var reverse=Guid.NewGuid();
        return new(new(id,Guid.NewGuid(),Guid.NewGuid(),"pokemon","Example set","Golisopod",number,language,null,null,[new(normal,"normal","Normal"),new(reverse,"reverse","Reverse")]),140,"Stage1",null,
            [new(normal,"normal","Normal","normal",null),new(reverse,"reverse","Reverse","reverse",null)]);
    }
    [Fact] public void ProviderPortugueseAndVisibleBrazilianPortugueseResolveWithoutAcceptingAnotherRegion()
    {
        var card=Printing("026/86","pt");var evidence=Evidence("026/086");
        Assert.Equal(card.Printing.PrintingId,new VisionPrintingResolver().Resolve([card],evidence).PrintingId);
        Assert.Null(new VisionPrintingResolver().Resolve([Printing("026/86","pt-PT")],evidence).PrintingId);
    }

    [Fact] public void ExactRegionalLanguagePrecedesItsLegacyProviderAlias()
    {
        var legacy=Printing("026/86","pt");var regional=Printing("026/86","pt-BR");
        Assert.Equal(regional.Printing.PrintingId,new VisionPrintingResolver().Resolve([legacy,regional],Evidence("026/086")).PrintingId);
    }

}
