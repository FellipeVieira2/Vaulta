using Vaulta.App.Core.Catalog;
using Xunit;
namespace Vaulta.App.Core.UnitTests.Vision;
public class ScannerRevealPresentationTests
{
    private static ScannerSessionCard Card(decimal? amount=289.90m)=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Charizard ex","Obsidian Flames","223/197","Holo","UNKNOWN",null,amount is null?null:new(amount.Value,"TCGplayer",DateTimeOffset.UtcNow),DateTimeOffset.UtcNow);
    [Fact] public void AvailableQuoteAnimatesFromZeroToExactApiValueAndTotal()
    {var p=ScannerRevealPresentation.Create(Card(),741.50m,1031.40m);Assert.Equal(ScannerRevealKind.Price,p.Kind);Assert.Equal(0m,p.ValueAt(0));Assert.Equal(289.90m,p.ValueAt(1));Assert.Equal(1031.40m,p.TotalAt(1));}
    [Fact] public void MissingQuoteDoesNotMeanZero()
    {var p=ScannerRevealPresentation.Create(Card(null),10,10);Assert.Equal(ScannerRevealKind.NoPrice,p.Kind);Assert.Null(p.ValueAt(.5));}
    [Fact] public void PendingVariantDoesNotRevealAnotherVariantsQuote()
    {var p=ScannerRevealPresentation.Create(Card() with{VariantId=null},0,0);Assert.Equal(ScannerRevealKind.VariantPending,p.Kind);Assert.Null(p.ValueAt(1));}
    [Fact] public void GradedCardNeverRevealsRawValue()
    {var card=Card() with{VisualIdentification=new("Charizard",null,null,null,.99,Certification:new("PSA","10","private"))};var p=ScannerRevealPresentation.Create(card,0,0);Assert.Equal(ScannerRevealKind.GradedUnavailable,p.Kind);Assert.Null(p.ValueAt(1));}
    [Fact] public void DuplicatePersistenceDoesNotAddAnotherValueDuringPresentation()
    {var card=Card();var s=ScannerSession.Start(Guid.NewGuid(),DateTimeOffset.UtcNow).Add(card).Add(card);var p=ScannerRevealPresentation.Create(card,0,s.EstimatedValueBrl);Assert.Equal(289.90m,p.TotalAt(1));Assert.Single(s.Cards);}
    [Fact] public void ARealAvailableZeroQuoteCanBeShown()
    {Assert.Equal(ScannerRevealKind.Price,ScannerRevealPresentation.Create(Card(0),10,10).Kind);}
}
