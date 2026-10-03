using Vaulta.App.Core.Vision;
using Vaulta.Vision.Contracts;
using Xunit;
namespace Vaulta.App.Core.UnitTests.Vision;
public class VisionParticipationTests
{
 private static VisionPoliciesDto Policy=>new(true,"ops-v1","improve-v1",7);
 [Fact] public void DefaultHasNoStorageOrImprovementPermission(){var p=new VisionParticipation();Assert.False(p.CanStore(Policy));Assert.False(p.CanImprove(Policy));}
 [Fact] public void StorageConsentDoesNotGrantImprovement(){var p=new VisionParticipation("ops-v1");Assert.True(p.CanStore(Policy));Assert.False(p.CanImprove(Policy));}
 [Fact] public void PolicyChangeDisablesPreviousPermission(){var p=new VisionParticipation("ops-v1","improve-v1");Assert.False(p.CanStore(Policy with {OperationalPolicyVersion="ops-v2"}));Assert.False(p.CanImprove(Policy with {ImprovementPolicyVersion="improve-v2"}));Assert.False(p.CanStore(Policy with {Enabled=false}));}
}
