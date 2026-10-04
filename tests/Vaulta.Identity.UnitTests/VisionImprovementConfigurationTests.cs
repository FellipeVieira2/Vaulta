using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Vision.Infrastructure;
using Xunit;
namespace Vaulta.Identity.UnitTests;

public class VisionImprovementConfigurationTests
{
    [Fact]
    public void PublicEnvironmentRejectsDeveloperAutoPromotion()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["Vision:Improvement:DeveloperAutoPromote"]="true",
            ["Vision:Improvement:DeveloperAccountIds:0"]=Guid.NewGuid().ToString(),
            ["Vision:History:Enabled"]="true",
            ["Vision:History:OperationalPolicyVersion"]="ops-v1",
            ["Vision:History:ImprovementPolicyVersion"]="improve-v1"
        }).Build();
        Assert.Throws<InvalidOperationException>(()=>new ServiceCollection().AddVisionModule(configuration));
    }
}
