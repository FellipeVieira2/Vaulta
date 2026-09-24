namespace Vaulta.Identity.Domain;

public sealed class RefreshToken
{
    private RefreshToken() { }
    public RefreshToken(Guid userId, string hash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = Guid.NewGuid(); UserId = userId; TokenHash = hash; CreatedAt = now; ExpiresAt = expiresAt; Version = Guid.NewGuid();
    }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public Guid Version { get; private set; }
    public void Revoke(DateTimeOffset now, Guid? replacement = null)
    {
        if (RevokedAt.HasValue) return;
        RevokedAt = now; ReplacedByTokenId = replacement; Version = Guid.NewGuid();
    }
}
