namespace Vaulta.Vision.Infrastructure;

public sealed class VisionImprovementOptions
{
    public bool DeveloperAutoPromote { get; set; }
    public Guid[] DeveloperAccountIds { get; set; } = [];
    public int RefreshDebounceSeconds { get; set; } = 2;

    public bool Allows(Guid owner) => DeveloperAutoPromote && owner != Guid.Empty && DeveloperAccountIds.Contains(owner);
    public void Validate(bool isDevelopment, bool historyEnabled)
    {
        if (RefreshDebounceSeconds is < 1 or > 5 || DeveloperAccountIds.Any(x => x == Guid.Empty)
            || DeveloperAutoPromote && (!isDevelopment || !historyEnabled || DeveloperAccountIds.Length == 0))
            throw new InvalidOperationException("Developer Vision improvement requires Development, enabled history, explicit developer accounts and a 1–5 second refresh window.");
    }
}
