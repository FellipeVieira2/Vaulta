namespace Vaulta.Assets.Domain;

public sealed class Asset
{
    public Guid Id { get; set; }
    public string ObjectKey { get; set; } = null!;
    public string Purpose { get; set; } = null!;
    public string Visibility { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long ContentLength { get; set; }
    public string? Sha256 { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? SourceUrl { get; set; }
    public Guid? OwnerId { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
