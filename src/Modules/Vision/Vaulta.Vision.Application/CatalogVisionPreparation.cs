using Vaulta.Catalog.Application;
namespace Vaulta.Vision.Application;
public sealed record CatalogVisionPreparationReport(CatalogArtifactImportReport Artwork,VisualReferenceBuildReport References)
{ public bool Complete=>Artwork.Failed==0 && Artwork.Missing==0 && References.Complete; }
public sealed class CatalogVisionPreparation(ICatalogArtifactImporter artwork,IVisualReferenceBuilder references)
{
    public async Task<CatalogVisionPreparationReport> PrepareAsync(Guid? setId,CancellationToken ct)
    {
        var imported=await artwork.ImportAsync(setId,ct);
        var indexed=await references.BuildAsync(setId,ct);
        return new(imported,indexed);
    }
}
