namespace Vaulta.Vision.Infrastructure;
public sealed class VisionHistoryOptions
{
 public bool Enabled {get;set;} public string? OperationalPolicyVersion {get;set;} public string? ImprovementPolicyVersion {get;set;} public int RetentionDays {get;set;}=7;
 public void Validate()
 { if(Enabled && (string.IsNullOrWhiteSpace(OperationalPolicyVersion) || OperationalPolicyVersion.Length>100 || string.IsNullOrWhiteSpace(ImprovementPolicyVersion) || ImprovementPolicyVersion.Length>100 || RetentionDays is <1 or >30)) throw new InvalidOperationException("Enabled Vision history requires versioned policies and 1-30 day retention."); }
}
