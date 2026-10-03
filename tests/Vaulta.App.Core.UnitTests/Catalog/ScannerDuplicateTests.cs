using Vaulta.App.Core.Catalog;
using Xunit;
namespace Vaulta.App.Core.UnitTests.Catalog;
public class ScannerDuplicateTests
{
 private static ScannerSessionCard Card(Guid printing,Guid? scan=null)=>new(scan??Guid.NewGuid(),printing,null,"Card","Set","1","Normal","UNKNOWN",null,new(10m,"test",DateTimeOffset.UtcNow),DateTimeOffset.UtcNow);
 [Fact] public void SecondOccurrenceOfSamePrintingRequiresConfirmation()
 {var id=Guid.NewGuid();var s=ScannerSession.Start(Guid.NewGuid(),DateTimeOffset.UtcNow).Add(Card(id));Assert.True(s.RequiresDuplicateConfirmation(Card(id)));Assert.Single(s.Cards);Assert.Equal(10m,s.EstimatedValueBrl);}
 [Fact] public void ReplayedSameOccurrenceDoesNotAskOrDoubleCount()
 {var card=Card(Guid.NewGuid());var s=ScannerSession.Start(Guid.NewGuid(),DateTimeOffset.UtcNow).Add(card);Assert.False(s.RequiresDuplicateConfirmation(card));Assert.Single(s.Add(card).Cards);}
 [Fact] public void DifferentPrintingWithSameNameDoesNotAskDuplicateQuestion()
 {var s=ScannerSession.Start(Guid.NewGuid(),DateTimeOffset.UtcNow).Add(Card(Guid.NewGuid()));Assert.False(s.RequiresDuplicateConfirmation(Card(Guid.NewGuid())));}
 [Fact] public void ConfirmedPhysicalCopyIsSeparateStockUnitAndAddsValue()
 {var id=Guid.NewGuid();var first=Card(id);var second=Card(id);var s=ScannerSession.Start(Guid.NewGuid(),DateTimeOffset.UtcNow).Add(first).Add(second);Assert.Equal(2,s.Cards.Count);Assert.Equal(20m,s.EstimatedValueBrl);Assert.NotEqual(s.Cards[0].ScanId,s.Cards[1].ScanId);}
}
