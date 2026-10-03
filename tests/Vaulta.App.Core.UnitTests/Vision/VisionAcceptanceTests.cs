using Vaulta.App.Core.Catalog;
using Vaulta.Vision.Contracts;
using Vaulta.Catalog.Contracts;
using Xunit;
namespace Vaulta.App.Core.UnitTests.Vision;
public class VisionAcceptanceTests
{
    private static readonly Guid Printing=Guid.NewGuid(), Variant=Guid.NewGuid();
    private static VisionScanResultDto Result(double certainty=.8,CardMarketQuoteDto? price=null)=>new(Guid.NewGuid(),"identified",Printing,Variant,certainty,.9,
        [new(new(Printing,Guid.NewGuid(),Guid.NewGuid(),"pokemon","Base Set","Alakazam","1/102","en",null,null,[new(Variant,"holo-unlimited","Holo")]),.9)],price,price is null?"unavailable":"available",null,new Dictionary<string,VisionFieldDto>(),null!);
    [Fact] public void EightyPercentKnownIdentityWithoutPriceCountsInSession()
    { var card=VisionAcceptance.CreateCard(Result()); Assert.NotNull(card); var session=ScannerSession.Start(Guid.NewGuid(),DateTimeOffset.UtcNow).Add(card!); Assert.Single(session.Cards); Assert.Equal(1,session.UnpricedCards); }
    [Fact] public void BelowThresholdRequiresReview()
    { Assert.Null(VisionAcceptance.CreateCard(Result(.79))); }
    [Fact] public void MatchingVariantPriceIsAddedExactlyOnce()
    { var now=DateTimeOffset.UtcNow;var r=Result(price:new(Variant,"Holo",115.63m,19.66m,"EUR","Cardmarket",now,5.8815m,now,[])); var card=VisionAcceptance.CreateCard(r)!;
      var s=ScannerSession.Start(Guid.NewGuid(),now).Add(card).Add(card); Assert.Equal(115.63m,s.EstimatedValueBrl); Assert.Single(s.Cards); }
    [Fact] public void AnotherVariantsPriceCannotEnterTotal()
    { var now=DateTimeOffset.UtcNow;var r=Result(price:new(Guid.NewGuid(),"Other",900m,900m,"BRL","test",now,1m,now,[])); Assert.Null(VisionAcceptance.CreateCard(r)!.MarketValue); }
    [Fact] public void GradedCardDoesNotBorrowRawPrice()
    { var now=DateTimeOffset.UtcNow;var r=Result(price:new(Variant,"Holo",115m,115m,"BRL","test",now,1m,now,[])) with {PriceStatus="graded_unavailable"}; Assert.Null(VisionAcceptance.CreateCard(r)!.MarketValue); }
    [Fact] public void VisibleGradingLabelIsKeptSeparateFromRawValuation()
    {var r=Result() with {Evidence=new Dictionary<string,VisionFieldDto>{{"isGraded",new("true",.95)},{"gradingCompany",new("PSA",.95)},{"grade",new("10",.95)},{"certificationNumber",new("123456",.95)}}};Assert.Equal("PSA",VisionAcceptance.CreateCard(r)!.VisualIdentification!.Certification!.Company);}
    [Fact] public void CorrectedSessionReplacesOccurrenceRatherThanCountingItTwice()
    {var now=DateTimeOffset.UtcNow;var card=VisionAcceptance.CreateCard(Result(price:new(Variant,"Holo",10m,10m,"BRL","test",now,1m,now,[])))!;var s=ScannerSession.Start(Guid.NewGuid(),now).Add(card).Complete();var corrected=card with {Name="Corrected",MarketValue=card.MarketValue! with {AmountBrl=25m}};var updated=s.Correct(corrected);Assert.Single(updated.Cards);Assert.Equal(25m,updated.EstimatedValueBrl);Assert.Equal(ScannerSessionPhase.Completed,updated.Phase);}
    [Fact] public void AmbiguousAndNonCardsCannotAutoEnterSession()
    { foreach(var status in new[]{"ambiguous","back","not_a_card","partial","needs_better_image"}) Assert.Null(VisionAcceptance.CreateCard(Result() with {Status=status})); }
}
