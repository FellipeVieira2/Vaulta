using Vaulta.SharedKernel;

namespace Vaulta.Payments.Domain;

public static class PayoutStatus
{
    public const string WaitingReceipt = "WAITING_RECEIPT";
    public const string WaitingSettlement = "WAITING_SETTLEMENT";
    public const string WaitingDestination = "WAITING_DESTINATION";
    public const string InsufficientNet = "INSUFFICIENT_NET";
    public const string Ready = "READY";
    public const string Submitting = "SUBMITTING";
    public const string Processing = "PROCESSING";
    public const string Done = "DONE";
    public const string Failed = "FAILED";
    public const string Blocked = "BLOCKED";
    public const string Reconciliation = "RECONCILIATION_REQUIRED";
}

/// <summary>One transfer obligation per order, never a withdrawable balance.</summary>
public sealed class SellerPayout
{
    private SellerPayout() { }
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid SellerId { get; private set; }
    public decimal ItemPriceBrl { get; private set; }
    public decimal PlatformFeeBrl { get; private set; }
    public decimal AmountBrl { get; private set; }
    public decimal? PaymentFeeBrl { get; private set; }
    public decimal? TransferFeeBrl { get; private set; }
    public decimal? SellerNetBrl { get; private set; }
    public string Currency { get; private set; } = "BRL";
    public string Status { get; private set; } = PayoutStatus.WaitingReceipt;
    public string ExternalReference { get; private set; } = null!;
    public string? TransferId { get; private set; }
    public Guid? DestinationVersion { get; private set; }
    public string? PixKey { get; private set; }
    public string? PixKeyType { get; private set; }
    public string? DestinationHolderName { get; private set; }
    public string? DestinationHolderDocument { get; private set; }
    public Guid? DestinationVerifiedBy { get; private set; }
    public DateTimeOffset? DestinationVerifiedAt { get; private set; }
    public string? DestinationEvidenceReference { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset NextCheckAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid Version { get; private set; }

    public static SellerPayout Create(Guid orderId, Guid paymentId, Guid sellerId,
        decimal itemPrice, decimal sellerFee, DateTimeOffset now)
    {
        if (orderId == Guid.Empty || paymentId == Guid.Empty || sellerId == Guid.Empty || itemPrice <= 0
            || sellerFee != Math.Round(itemPrice * 0.08m, 2) || sellerFee >= itemPrice)
            throw new DomainException("Payout requires an order and the agreed seller-only 8% fee.");
        var id = Guid.NewGuid();
        return new SellerPayout
        {
            Id = id, OrderId = orderId, PaymentId = paymentId, SellerId = sellerId,
            ItemPriceBrl = itemPrice, PlatformFeeBrl = sellerFee, AmountBrl = itemPrice - sellerFee,
            ExternalReference = $"vaulta_payout_{id:N}", CreatedAt = now, UpdatedAt = now,
            NextCheckAt = now, Version = Guid.NewGuid()
        };
    }

    public void Evaluate(bool receiptConfirmed, bool settled, decimal netAmount,
        SellerPixDestination? destination, bool blocked, DateTimeOffset now)
    {
        if (SubmittedAt.HasValue || Status is PayoutStatus.Done or PayoutStatus.Failed or PayoutStatus.Reconciliation)
            return;
        if (settled)
        {
            if (netAmount < 0 || netAmount > ItemPriceBrl || netAmount != Math.Round(netAmount, 2))
                throw new DomainException("Invalid settled payment amount.");
            PaymentFeeBrl = ItemPriceBrl - netAmount;
            AmountBrl = Math.Max(0, netAmount - PlatformFeeBrl);
        }
        Status = blocked ? PayoutStatus.Blocked
            : !receiptConfirmed ? PayoutStatus.WaitingReceipt
            : !settled ? PayoutStatus.WaitingSettlement
            : destination is null || !destination.IsVerified ? PayoutStatus.WaitingDestination
            : AmountBrl <= 0 || netAmount < AmountBrl ? PayoutStatus.InsufficientNet : PayoutStatus.Ready;
        // Destination is frozen only by the persisted, atomic submission claim.
        PixKey = Status == PayoutStatus.Ready ? destination!.Key : null;
        PixKeyType = Status == PayoutStatus.Ready ? destination!.KeyType : null;
        DestinationVersion = Status == PayoutStatus.Ready ? destination!.Version : null;
        DestinationHolderName = Status == PayoutStatus.Ready ? destination!.HolderName : null;
        DestinationHolderDocument = Status == PayoutStatus.Ready ? destination!.HolderDocument : null;
        DestinationVerifiedBy = Status == PayoutStatus.Ready ? destination!.VerifiedBy : null;
        DestinationVerifiedAt = Status == PayoutStatus.Ready ? destination!.VerifiedAt : null;
        DestinationEvidenceReference = Status == PayoutStatus.Ready ? destination!.IdentityEvidenceReference : null;
        CheckLater(now);
    }

    public void MarkClaimed(DateTimeOffset now, Guid version)
    {
        if (Status != PayoutStatus.Ready || PixKey is null || PixKeyType is null)
            throw new DomainException("Only an eligible payout can be submitted.");
        Status = PayoutStatus.Submitting;
        SubmittedAt = now;
        Version = version;
        UpdatedAt = now;
        NextCheckAt = now.AddMinutes(2);
    }

    public void RequireReconciliation(DateTimeOffset now)
    {
        if (Status is PayoutStatus.Done or PayoutStatus.Failed) return;
        Status = PayoutStatus.Reconciliation;
        CheckLater(now);
    }

    public void ApplyTransfer(string transferId, string providerStatus, decimal amount,
        string externalReference, DateTimeOffset now, decimal? netValue = null, decimal? transferFee = null)
    {
        if (!SubmittedAt.HasValue || string.IsNullOrWhiteSpace(transferId)
            || (TransferId is not null && TransferId != transferId)
            || amount != AmountBrl || externalReference != ExternalReference
            || !netValue.HasValue || !transferFee.HasValue || netValue < 0 || transferFee < 0
            || netValue + transferFee != amount || netValue != Math.Round(netValue.Value, 2)
            || transferFee != Math.Round(transferFee.Value, 2))
            throw new ConflictException("Transfer does not match the recorded payout.");
        var newStatus = providerStatus switch
        {
            "DONE" => PayoutStatus.Done,
            "FAILED" or "CANCELLED" => PayoutStatus.Failed,
            "PENDING" or "IN_BANK_PROCESSING" or "BLOCKED" => PayoutStatus.Processing,
            _ => throw new DomainException("Unknown transfer status.")
        };
        TransferId = transferId;
        // Out-of-order events cannot undo a definitive outcome. Conflicting terminal
        // outcomes require investigation, never an automatic second transfer.
        if (Status is PayoutStatus.Done or PayoutStatus.Failed)
        {
            if (newStatus is PayoutStatus.Done or PayoutStatus.Failed && newStatus != Status)
                throw new ConflictException("Conflicting final transfer status.");
            if (newStatus == Status && (TransferFeeBrl != transferFee || SellerNetBrl != netValue))
                throw new ConflictException("Conflicting final transfer amounts.");
            return;
        }
        TransferFeeBrl = transferFee;
        SellerNetBrl = netValue;
        Status = newStatus;
        if (newStatus == PayoutStatus.Done) CompletedAt = now;
        CheckLater(now);
    }

    private void CheckLater(DateTimeOffset now)
    {
        UpdatedAt = now;
        NextCheckAt = now.AddMinutes(1);
        Version = Guid.NewGuid();
    }
}

public sealed class SellerPixDestination
{
    private SellerPixDestination() { }
    public Guid SellerId { get; private set; }
    public string Key { get; private set; } = null!;
    public string KeyType { get; private set; } = null!;
    public string HolderName { get; private set; } = null!;
    public string HolderDocument { get; private set; } = null!;
    public bool IsVerified { get; private set; }
    public Guid? VerifiedBy { get; private set; }
    public string? IdentityEvidenceReference { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public Guid Version { get; private set; }

    public static SellerPixDestination Register(Guid sellerId, string key, string type,
        string holderName, string holderDocument, DateTimeOffset now)
    {
        var destination = new SellerPixDestination { SellerId = sellerId };
        destination.Replace(key, type, holderName, holderDocument, now);
        return destination;
    }

    public void Replace(string key, string type, string holderName, string holderDocument, DateTimeOffset now)
    {
        if (SellerId == Guid.Empty || string.IsNullOrWhiteSpace(key) || key.Length > 200
            || type is not ("CPF" or "CNPJ" or "EMAIL" or "PHONE" or "EVP")
            || string.IsNullOrWhiteSpace(holderName) || holderName.Length > 200
            || string.IsNullOrWhiteSpace(holderDocument) || holderDocument.Length > 30)
            throw new DomainException("A valid Pix destination and provider holder information are required.");
        Key = key.Trim(); KeyType = type; HolderName = holderName; HolderDocument = holderDocument;
        IsVerified = false; VerifiedBy = null; VerifiedAt = null; IdentityEvidenceReference = null;
        UpdatedAt = now; Version = Guid.NewGuid();
    }

    // Caller must be a trusted operator who has checked seller identity against
    // the provider holder. A masked lookup or a seller checkbox is not identity proof.
    public void Verify(Guid operatorId, Guid expectedVersion, string evidenceReference, DateTimeOffset now)
    {
        if (expectedVersion != Version) throw new ConflictException("Pix destination changed. Review the current key.");
        if (operatorId == Guid.Empty || operatorId == SellerId || string.IsNullOrWhiteSpace(evidenceReference)
            || evidenceReference.Trim().Length is < 5 or > 200)
            throw new DomainException("Independent identity verification evidence is required.");
        IsVerified = true; VerifiedBy = operatorId; IdentityEvidenceReference = evidenceReference.Trim();
        VerifiedAt = now; UpdatedAt = now; Version = Guid.NewGuid();
    }
}

public sealed class TransferWebhookReceipt
{
    private TransferWebhookReceipt() { }
    public string Id { get; private set; } = null!;
    public Guid PayoutId { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public static TransferWebhookReceipt Create(string id, Guid payoutId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200) throw new DomainException("Webhook event ID is required.");
        return new TransferWebhookReceipt { Id = id, PayoutId = payoutId, ReceivedAt = now };
    }
}
