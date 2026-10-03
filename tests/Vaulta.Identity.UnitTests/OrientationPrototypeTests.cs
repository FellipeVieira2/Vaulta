using Vaulta.Vision.Encoding;
using Vaulta.Vision.Contracts;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public class OrientationPrototypeTests
{
    private static EncoderIdentity Identity=>new("test","revision","sha","preprocess",3,"runtime","input","output","hash");
    [Fact] public void SimilarScoresRemainUnknownInsteadOfInventingFront()
    { var p=new OrientationPrototypes(Identity,new Dictionary<string,float[]>{{"front",[1,0,0]},{"back",[0,1,0]},{"no-card",[0,0,1]}}); Assert.Equal("unknown",p.Classify(new(Identity,[.70710677f,.70710677f,0]))); }
    [Fact] public void ClearBackAndNonCardHaveSeparateLabels()
    { var p=new OrientationPrototypes(Identity,new Dictionary<string,float[]>{{"front",[1,0,0]},{"back",[0,1,0]},{"no-card",[0,0,1]}}); Assert.Equal("back",p.Classify(new(Identity,[0,1,0])));Assert.Equal("no-card",p.Classify(new(Identity,[0,0,1]))); }
    [Fact] public void IncompatibleModelNeverComparesVectors()
    { var p=new OrientationPrototypes(Identity,new Dictionary<string,float[]>{{"front",[1,0,0]},{"back",[0,1,0]},{"no-card",[0,0,1]}}); Assert.Throws<InvalidDataException>(()=>p.Classify(new(Identity with {Revision="other"},[1,0,0]))); }
}
