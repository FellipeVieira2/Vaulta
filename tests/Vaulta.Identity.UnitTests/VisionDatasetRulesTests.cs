using Vaulta.Vision.Domain;
using Vaulta.SharedKernel;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public class VisionDatasetRulesTests
{
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(-.1)] [InlineData(1.1)]
    public void ConfidenceRejectsInvalidValues(double value)=>Assert.Throws<DomainException>(()=>VisionDatasetRules.ValidateConfidence(value));
    [Fact] public void PublicFeedbackCannotClaimAdminReview()=>Assert.Throws<DomainException>(()=>VisionDatasetRules.ValidateFeedback("AdminReview",null,null,"front","card-present",null));
    [Fact] public void VariantRequiresPrinting()=>Assert.Throws<DomainException>(()=>VisionDatasetRules.ValidateFeedback("UserCorrection",null,Guid.NewGuid(),null,null,null));
    [Theory] [InlineData("front","card-present")] [InlineData("back","card-present")] [InlineData("unknown","no-card")]
    public void OrientationAndPresenceCanBeLabeledWithoutIdentity(string orientation,string presence)=>VisionDatasetRules.ValidateFeedback("UserCorrection",null,null,orientation,presence,null);
    [Fact] public void OperationalPermissionIsNotImprovementPermission()=>Assert.Throws<DomainException>(()=>VisionDatasetRules.RequirePromotion(false,true,true));
    [Fact] public void AutoAcceptanceCannotBecomeVerifiedGroundTruth()=>Assert.Throws<DomainException>(()=>VisionDatasetRules.RequirePromotion(true,false,true));
    [Fact] public void MissingMatchingCaptureCannotBePromoted()=>Assert.Throws<DomainException>(()=>VisionDatasetRules.RequirePromotion(true,true,false));
}
