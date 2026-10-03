using Vaulta.Catalog.Application;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public sealed class CatalogVisionPreparationTests
{
    [Theory]
    [InlineData(0,0,true)]
    [InlineData(1,0,false)]
    [InlineData(0,1,false)]
    public async Task ArtworkIsIngestedBeforeReferencesAndPendingWorkIsNotComplete(int missing,int failed,bool complete)
    {
        var sequence=new List<string>(); var set=Guid.NewGuid();
        var preparation=new CatalogVisionPreparation(new Artwork(sequence,set,missing),new References(sequence,set,failed));
        var report=await preparation.PrepareAsync(set,default);
        Assert.Equal(new[]{"artwork","references"},sequence); Assert.Equal(complete,report.Complete);
    }
    private sealed class Artwork(List<string> calls,Guid expected,int missing) : ICatalogArtifactImporter
    {
        public Task<CatalogArtifactImportReport> ImportAsync(Guid? setId,CancellationToken ct)
        { Assert.Equal(expected,setId); calls.Add("artwork"); return Task.FromResult(new CatalogArtifactImportReport(1,0,missing,0,0)); }
    }
    private sealed class References(List<string> calls,Guid expected,int failed) : IVisualReferenceBuilder
    {
        public Task<VisualReferenceBuildReport> BuildAsync(Guid? setId,CancellationToken ct)
        { Assert.Equal(expected,setId); calls.Add("references"); return Task.FromResult(new VisualReferenceBuildReport(1,0,0,failed,new("test",1,new("fixture","fixture","fixture","fixture",3,"test","input","output")))); }
        public Task<VisualIndexStatus> LoadAsync(CancellationToken ct)=>throw new NotSupportedException();
    }
}
