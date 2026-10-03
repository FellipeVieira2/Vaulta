namespace Vaulta.Catalog.Application;
public interface ICatalogArtifactImporter
{
    Task<CatalogArtifactImportReport> ImportAsync(Guid? setId,CancellationToken ct);
}
public sealed record CatalogArtifactImportReport(int Ready,int Unchanged,int Missing,int Failed,int Priced);
