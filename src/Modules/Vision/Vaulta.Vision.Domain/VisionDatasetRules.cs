using Vaulta.SharedKernel;
namespace Vaulta.Vision.Domain;
public static class VisionDatasetRules
{
    public static void ValidateConfidence(double? value)
    { if(value is { } v && (!double.IsFinite(v) || v is <0 or >1)) throw new DomainException("Invalid confidence."); }
    public static void ValidateFeedback(string source,Guid? printing,Guid? variant,string? orientation,string? presence,string? notes)
    {
        if(source is not ("UserConfirmation" or "UserCorrection") || printing==Guid.Empty || variant==Guid.Empty || variant is not null && printing is null
            || orientation is not (null or "front" or "back" or "unknown") || presence is not (null or "card-present" or "no-card" or "multiple-cards" or "uncertain")
            || notes?.Length>1000 || printing is null && orientation is null && presence is null) throw new DomainException("Invalid feedback.");
    }
    public static void RequirePromotion(bool consent,bool humanFeedback,bool matchingCapture)
    { if(!consent || !humanFeedback || !matchingCapture) throw new DomainException("Promotion requires improvement permission, human review and a matching ready capture."); }
    public static void ValidateKey(string key)
    { if(string.IsNullOrWhiteSpace(key) || key.Length>128 || key.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='-' && c!='_')) throw new DomainException("Invalid execution key."); }
    public static string Sha(string value)
    { if(value.Length!=64 || !value.All(Uri.IsHexDigit)) throw new DomainException("Invalid image SHA256."); return value.ToLowerInvariant(); }
}
