using Vaulta.SharedKernel;

namespace Vaulta.Payments.Domain;

public static class PaymentRules
{
    public const string PendingStatus = "pending";
    public const string OverdueStatus = "overdue";
    public const string ConfirmedStatus = "confirmed";
    public const string FailedStatus = "failed";
    public const string RefundedStatus = "refunded";

    public const string BillingTypePix = "PIX";
    public const string BillingTypeCreditCard = "CREDIT_CARD";
    public const string BillingTypeBoleto = "BOLETO";

    public static string ValidateBillingType(string? billingType)
    {
        if (string.IsNullOrWhiteSpace(billingType))
            throw new DomainException("Billing type is required.");
        var normalized = billingType.Trim().ToUpperInvariant();
        if (normalized is not (BillingTypePix or BillingTypeCreditCard or BillingTypeBoleto))
            throw new DomainException($"Billing type must be {BillingTypePix}, {BillingTypeCreditCard} or {BillingTypeBoleto}.");
        return normalized;
    }

    public static decimal ValidateAmount(decimal amount)
    {
        if (amount <= 0) throw new DomainException("Payment amount must be greater than zero.");
        return Math.Round(amount, 2);
    }
}
