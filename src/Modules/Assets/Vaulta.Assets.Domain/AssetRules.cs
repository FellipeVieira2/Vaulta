using Vaulta.SharedKernel;

namespace Vaulta.Assets.Domain;

public static class AssetRules
{
    private static readonly HashSet<string> AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly HashSet<string> AllowedPurposes = ["collection-item", "profile-avatar", "vision-scan"];

    public static void ValidateUpload(string purpose, string contentType, long contentLength, string? sha256)
    {
        if (!AllowedPurposes.Contains(purpose)) throw new DomainException("Unsupported asset purpose.");
        if (!AllowedContentTypes.Contains(contentType)) throw new DomainException("Unsupported asset content type.");
        if (contentLength is <= 0 or > 15_000_000) throw new DomainException("Asset size must be between 1 byte and 15 MB.");
        if (sha256 is not null && (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))) throw new DomainException("SHA-256 must be 64 hexadecimal characters.");
    }
}
