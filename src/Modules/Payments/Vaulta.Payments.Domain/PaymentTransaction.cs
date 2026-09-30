using Vaulta.SharedKernel;

namespace Vaulta.Payments.Domain;

public sealed record PaymentCreatedDomainEvent(Guid Id, Guid PaymentId, Guid OrderId, decimal Amount, string BillingType, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record PaymentConfirmedDomainEvent(Guid Id, Guid PaymentId, Guid OrderId, string AsaasPaymentId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record PaymentFailedDomainEvent(Guid Id, Guid PaymentId, Guid OrderId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record PaymentRefundedDomainEvent(Guid Id, Guid PaymentId, Guid OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class PaymentTransaction : AggregateRoot
{
    private readonly List<PaymentSplit> _splits = [];
    private PaymentTransaction() { }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid SellerId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal NetAmount { get; private set; }
    public string Currency { get; private set; } = "BRL";
    public string BillingType { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? AsaasPaymentId { get; private set; }
    public string? AsaasCustomerId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public string? PixQrCode { get; private set; }
    public string? BankSlipUrl { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public Guid Version { get; private set; }
    public IReadOnlyCollection<PaymentSplit> Splits => _splits.AsReadOnly();

    public static PaymentTransaction Create(
        Guid orderId,
        Guid buyerId,
        Guid sellerId,
        decimal amount,
        string billingType,
        string? asaasCustomerId,
        DateTimeOffset now)
    {
        if (orderId == Guid.Empty || buyerId == Guid.Empty || sellerId == Guid.Empty)
            throw new DomainException("Order, buyer and seller identifiers are required.");
        if (amount <= 0) throw new DomainException("Payment amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(billingType))
            throw new DomainException("Billing type is required.");

        var transaction = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Amount = Math.Round(amount, 2),
            NetAmount = Math.Round(amount, 2),
            BillingType = billingType.ToUpperInvariant(),
            Status = PaymentRules.PendingStatus,
            AsaasCustomerId = asaasCustomerId,
            CreatedAt = now,
            UpdatedAt = now,
            Version = Guid.NewGuid()
        };
        transaction.Raise(new PaymentCreatedDomainEvent(Guid.NewGuid(), transaction.Id, orderId, amount, billingType, now));
        return transaction;
    }

    public void AddSplit(string walletId, decimal? fixedValue, decimal? percentualValue, string? externalReference, string? description)
    {
        if (string.IsNullOrWhiteSpace(walletId))
            throw new DomainException("Split wallet identifier is required.");
        if (fixedValue.HasValue && fixedValue.Value < 0)
            throw new DomainException("Split fixed value cannot be negative.");
        if (percentualValue.HasValue && (percentualValue.Value < 0 || percentualValue.Value > 100))
            throw new DomainException("Split percentage must be between 0 and 100.");
        if (!fixedValue.HasValue && !percentualValue.HasValue)
            throw new DomainException("Split must have either a fixed value or a percentage.");

        _splits.Add(new PaymentSplit(Id, walletId, fixedValue, percentualValue, externalReference, description));
    }

    public void Confirm(string asaasPaymentId, decimal netAmount, DateTimeOffset now)
    {
        if (Status != PaymentRules.PendingStatus)
            throw new DomainException($"Payment cannot be confirmed from status '{Status}'.");
        if (string.IsNullOrWhiteSpace(asaasPaymentId))
            throw new DomainException("Asaas payment identifier is required.");

        AsaasPaymentId = asaasPaymentId;
        NetAmount = Math.Round(netAmount, 2);
        Status = PaymentRules.ConfirmedStatus;
        ConfirmedAt = now;
        Touch(now);
        Raise(new PaymentConfirmedDomainEvent(Guid.NewGuid(), Id, OrderId, asaasPaymentId, now));
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        if (Status == PaymentRules.ConfirmedStatus || Status == PaymentRules.RefundedStatus)
            throw new DomainException($"Payment cannot fail from status '{Status}'.");
        Status = PaymentRules.FailedStatus;
        FailureReason = reason;
        Touch(now);
        Raise(new PaymentFailedDomainEvent(Guid.NewGuid(), Id, OrderId, reason, now));
    }

    public void Refund(DateTimeOffset now)
    {
        if (Status != PaymentRules.ConfirmedStatus)
            throw new DomainException($"Only confirmed payments can be refunded.");
        Status = PaymentRules.RefundedStatus;
        Touch(now);
        Raise(new PaymentRefundedDomainEvent(Guid.NewGuid(), Id, OrderId, now));
    }

    public void SetCheckoutInfo(string? checkoutUrl, string? pixQrCode, string? bankSlipUrl)
    {
        CheckoutUrl = checkoutUrl;
        PixQrCode = pixQrCode;
        BankSlipUrl = bankSlipUrl;
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}

public sealed class PaymentSplit
{
    private PaymentSplit() { }
    internal PaymentSplit(Guid paymentId, string walletId, decimal? fixedValue, decimal? percentualValue, string? externalReference, string? description)
    {
        PaymentId = paymentId;
        WalletId = walletId;
        FixedValue = fixedValue;
        PercentualValue = percentualValue;
        ExternalReference = externalReference;
        Description = description;
    }

    public Guid PaymentId { get; private set; }
    public string WalletId { get; private set; } = null!;
    public decimal? FixedValue { get; private set; }
    public decimal? PercentualValue { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? Description { get; private set; }
}