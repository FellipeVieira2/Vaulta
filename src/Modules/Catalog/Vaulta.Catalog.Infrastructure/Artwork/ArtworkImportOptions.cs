namespace Vaulta.Catalog.Infrastructure.Artwork;

public sealed class ArtworkImportOptions
{
    public int WorkerCount { get; set; } = 3;
    public int PageSize { get; set; } = 200;
    public int RevalidateAfterHours { get; set; } = 24;
}