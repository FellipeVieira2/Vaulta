using System.Text.RegularExpressions;
using Vaulta.SharedKernel;

namespace Vaulta.Orders.Domain;

public static partial class OrderRules
{
    public const string PendingStatus = "pending";
    public const string PaidStatus = "paid";
    public const string ShippedStatus = "shipped";
    public const string DeliveredStatus = "delivered";
    public const string CancelledStatus = "cancelled";
    public const string RefundedStatus = "refunded";
    public const string RefundPendingStatus = "refund_pending";

    public const string ReservationActiveStatus = "active";
    public const string ReservationExpiredStatus = "expired";
    public const string ReservationReleasedStatus = "released";
    public const string ReservationConsumedStatus = "consumed";

    public static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(15);

    public static string ValidateCondition(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) throw new DomainException("Condition is required.");
        var trimmed = condition.Trim();
        if (trimmed.Length > 32) throw new DomainException("Condition must be at most 32 characters.");
        return trimmed;
    }

    public static decimal ValidatePrice(decimal priceBrl)
    {
        if (priceBrl <= 0) throw new DomainException("Price must be greater than zero.");
        return Math.Round(priceBrl, 2);
    }

    public static string? ValidateAddress(string? value, string fieldName, int maxLength = 200)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > maxLength) throw new DomainException($"{fieldName} must be at most {maxLength} characters.");
        return trimmed;
    }

    [GeneratedRegex(@"^\d{5}-?\d{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex ZipCodePattern();

    public static string? ValidateZipCode(string? zipCode)
    {
        if (zipCode is null) return null;
        var trimmed = zipCode.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > 10) throw new DomainException("Zip code must be at most 10 characters.");
        if (!ZipCodePattern().IsMatch(trimmed))
            throw new DomainException("Invalid zip code format. Expected 00000-000 or 00000000.");
        return trimmed;
    }
}
