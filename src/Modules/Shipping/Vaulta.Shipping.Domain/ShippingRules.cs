using Vaulta.SharedKernel;

namespace Vaulta.Shipping.Domain;

public static class ShippingRules
{
    public const string CreatedStatus = "created";
    public const string InTransitStatus = "in_transit";
    public const string OutForDeliveryStatus = "out_for_delivery";
    public const string DeliveredStatus = "delivered";
    public const string FailedStatus = "failed";
    public const string ReturnedStatus = "returned";

    public static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) throw new DomainException("Shipping status is required.");
        var normalized = status.Trim().ToLowerInvariant();
        if (normalized is not (CreatedStatus or InTransitStatus or OutForDeliveryStatus or DeliveredStatus or FailedStatus or ReturnedStatus))
            throw new DomainException($"Invalid shipping status: {status}");
        return normalized;
    }

    public static string ValidateCarrier(string? carrier)
    {
        if (string.IsNullOrWhiteSpace(carrier)) throw new DomainException("Carrier is required.");
        var trimmed = carrier.Trim();
        if (trimmed.Length > 100) throw new DomainException("Carrier name must be at most 100 characters.");
        return trimmed.ToUpperInvariant();
    }

    public static string? ValidateTrackingCode(string? trackingCode)
    {
        if (trackingCode is null) return null;
        var trimmed = trackingCode.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > 100) throw new DomainException("Tracking code must be at most 100 characters.");
        return trimmed;
    }

    public static string? ValidateZipCode(string? zipCode)
    {
        if (zipCode is null) return null;
        var trimmed = zipCode.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > 10) throw new DomainException("Zip code must be at most 10 characters.");
        return trimmed;
    }

    public static decimal? ValidateCost(decimal? cost)
    {
        if (cost.HasValue && cost.Value < 0) throw new DomainException("Shipping cost cannot be negative.");
        return cost.HasValue ? Math.Round(cost.Value, 2) : null;
    }
}