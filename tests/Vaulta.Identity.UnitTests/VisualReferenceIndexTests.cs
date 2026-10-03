using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Domain;
using Xunit;
namespace Vaulta.Identity.UnitTests;
// Artificial 3D vectors exercise math/versioning, never claim visual-model accuracy.
public sealed class VisualReferenceIndexTests
{
    private static readonly EncoderIdentity Model=new("artificial-test-encoder","r1","hash","preprocess-v1",3,"test","input","output");
    [Theory]
    [InlineData(0f,0f,0f)]
    [InlineData(float.NaN,1f,0f)]
    [InlineData(float.PositiveInfinity,1f,0f)]
    public void RejectsInvalidVectors(float a,float b,float c)=>Assert.Throws<ArgumentException>(()=>EmbeddingMath.Normalize([a,b,c],3));
    [Fact]
    public void RejectsDimensionMismatch()=>Assert.Throws<ArgumentException>(()=>EmbeddingMath.Normalize([1f,0f],3));
    [Fact]
    public async Task RanksByCosineAndCopiesVectorsAtPublication()
    {
        var close=Guid.NewGuid(); var far=Guid.NewGuid(); var vector=new[]{2f,0f,0f}; var index=new CosineReferenceIndex(Model);
        index.Publish(Model,"v1",[new(Guid.NewGuid(),far,[0f,1f,0f],"official"),new(Guid.NewGuid(),close,vector,"official")]);
        vector[0]=0f; vector[1]=10f;
        var matches=await index.SearchAsync(new(Model,[5f,0f,0f]),2,default);
        Assert.Equal(close,matches[0].PrintingId); Assert.Equal(1d,matches[0].Similarity,5); Assert.Equal(2,index.Status.ReferenceCount);
    }
    [Fact]
    public async Task RejectsForeignModelAndPreprocessingInsteadOfComparingSilently()
    {
        var index=new CosineReferenceIndex(Model); index.Publish(Model,"v1",[new(Guid.NewGuid(),Guid.NewGuid(),[1f,0f,0f],"official")]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>index.SearchAsync(new(Model with { PreprocessingVersion="v2" },[1f,0f,0f]),1,default));
        Assert.Throws<InvalidOperationException>(()=>index.Publish(Model with { WeightsSha256="changed" },"v2",[]));
        Assert.Equal("v1",index.Status.Version);
    }
    [Fact]
    public void InvalidReplacementLeavesOldSnapshotActiveAndCapacityIsBounded()
    {
        var index=new CosineReferenceIndex(Model,1); var entry=new VisualIndexEntry(Guid.NewGuid(),Guid.NewGuid(),[1f,0f,0f],"official");
        index.Publish(Model,"v1",[entry]);
        Assert.Throws<ArgumentException>(()=>index.Publish(Model,"v2",[entry with { Vector=[float.NaN,0f,0f] }]));
        Assert.Throws<InvalidOperationException>(()=>index.Publish(Model,"v2",[entry,entry with { ReferenceId=Guid.NewGuid() }]));
        Assert.Equal("v1",index.Status.Version);
    }
}
