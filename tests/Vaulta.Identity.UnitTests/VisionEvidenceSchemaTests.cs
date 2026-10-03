using System.Text.Json;
using Vaulta.Catalog.Infrastructure.Recognition;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public sealed class VisionEvidenceSchemaTests
{
    [Theory] [InlineData("front","true","unlimited")] [InlineData("back","true",null)] [InlineData(null,"false",null)]
    public void OrientationAndEditionRemainStructuredEvidence(string? side,string card,string? edition)
    {
        var fields=new Dictionary<string,object?> { ["schemaVersion"]=5 };
        foreach(var key in new[]{"gameCode","name","collectorNumber","setCode","setName","language","variant","hp","finish","condition","isGraded","gradingCompany","grade","certificationNumber","rarity","year","cardType","stage","cardSide","isCard","edition"})
        {
            var value=key switch { "cardSide"=>side,"isCard"=>card,"edition"=>edition,_=>null };
            fields[key]=new { value,confidence=value is null?0:.96 };
        }
        var result=CardEvidenceJsonParser.Parse(JsonSerializer.Serialize(fields),"test","test");
        Assert.NotNull(result); Assert.Equal(side,result.CardSide?.Value); Assert.Equal(card,result.IsCard?.Value); Assert.Equal(edition,result.Edition?.Value);
    }
}
