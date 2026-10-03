namespace Vaulta.Vision.Domain;
public sealed class VisualReference
{
    public Guid Id { get; set; }
    public Guid PrintingId { get; set; }
    public Guid? SourceAssetId { get; set; }
    public string ModelVersion { get; set; }=null!;
    public string ModelManifestJson { get; set; }=null!;
    public string Origin { get; set; }="official";
    public string Status { get; set; }="pending";
    public string? ImageSha256 { get; set; }
    public string? OriginalSourceSha256 { get; set; }
    public string? VectorSha256 { get; set; }
    public int Dimension { get; set; }
    public float[]? Vector { get; set; }
    public string? ErrorCategory { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
