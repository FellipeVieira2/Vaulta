using Vaulta.SharedKernel;

namespace Vaulta.Marketplace.Domain;

public static class MarketplaceRules
{
    public const string ActiveStatus = "active";
    public const string SuspendedStatus = "suspended";
    public const string CancelledStatus = "cancelled";
    public const string SoldStatus = "sold";
    public const string PublishingStatus = "publishing";
    public const string DraftStatus = "draft";

    public static string DraftCondition(string? condition)
    {
        var normalized = condition?.Trim().ToUpperInvariant();
        return normalized is "MINT" or "NEAR_MINT" or "LIGHTLY_PLAYED" or "MODERATELY_PLAYED" or "HEAVILY_PLAYED" or "DAMAGED" or "UNKNOWN"
            ? normalized : throw new DomainException("Declare a supported card condition.");
    }

    public static string OperationKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new DomainException("An operation key between 1 and 128 characters is required.");
        return key.Trim();
    }

    public const decimal MinPriceBrl = 1.00m;
    public const decimal MaxPriceBrl = 999_999.99m;
    public const decimal PlatformCommissionRate = 0.08m; // 8%

    public static string Condition(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) throw new DomainException("Condition is required.");
        var trimmed = condition.Trim();
        if (trimmed.Length > 32) throw new DomainException("Condition must be at most 32 characters.");
        return trimmed;
    }

    public static decimal Price(decimal priceBrl)
    {
        if (priceBrl < MinPriceBrl) throw new DomainException($"Price must be at least R$ {MinPriceBrl:N2}.");
        if (priceBrl > MaxPriceBrl) throw new DomainException($"Price must be at most R$ {MaxPriceBrl:N2}.");
        return Math.Round(priceBrl, 2);
    }

    public static string? Description(string? description)
    {
        if (description is null) return null;
        var trimmed = description.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > 2000) throw new DomainException("Description must be at most 2000 characters.");
        return trimmed;
    }

    public static string? Bio(string? bio)
    {
        if (bio is null) return null;
        var trimmed = bio.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > 500) throw new DomainException("Bio must be at most 500 characters.");
        return trimmed;
    }

    public static string? AddressField(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > 200) throw new DomainException("Address fields must be at most 200 characters.");
        return trimmed;
    }

    public static string? ZipCode(string? zipCode)
    {
        if (zipCode is null) return null;
        var trimmed = zipCode.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > 10) throw new DomainException("Zip code must be at most 10 characters.");
        return trimmed;
    }

    public static string PhotoType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) throw new DomainException("Photo type is required.");
        var normalized = type.Trim().ToUpperInvariant();
        if (normalized is not ("FRONT" or "BACK" or "DETAIL" or "OTHER"))
            throw new DomainException("Photo type must be FRONT, BACK, DETAIL or OTHER.");
        return normalized;
    }

    public static decimal CalculatePlatformFee(decimal priceBrl) => Math.Round(priceBrl * PlatformCommissionRate, 2);
    public static decimal CalculateSellerPayout(decimal priceBrl) => Math.Round(priceBrl - CalculatePlatformFee(priceBrl), 2);
}
