using Vaulta.Vision.Contracts;
namespace Vaulta.App.Core.Vision;
public sealed record VisionParticipation(string? OperationalPolicyVersion=null,string? ImprovementPolicyVersion=null)
{
 public bool CanStore(VisionPoliciesDto policy)=>policy.Enabled && OperationalPolicyVersion is not null && OperationalPolicyVersion==policy.OperationalPolicyVersion;
 public bool CanImprove(VisionPoliciesDto policy)=>CanStore(policy) && ImprovementPolicyVersion is not null && ImprovementPolicyVersion==policy.ImprovementPolicyVersion;
}
