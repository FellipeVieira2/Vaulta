namespace Vaulta.Vision.Application;

public sealed record CatalogVisionPreparationReport(VisualReferenceBuildReport References)
{
    public bool Complete => References.Complete;
}

public sealed class CatalogVisionPreparation(IVisualReferenceBuilder references)
{
    public async Task<CatalogVisionPreparationReport> PrepareAsync(Guid? setId, CancellationToken ct)
    {
        var indexed = await references.BuildAsync(setId, ct);
        return new(indexed);
    }
}