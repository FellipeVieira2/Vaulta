using Vaulta.SharedKernel;

namespace Vaulta.Payments.Domain;

/// <summary>Private payer record bound to one authenticated Vaulta user.</summary>
public sealed class BuyerCustomer
{
    private BuyerCustomer() { }
    public Guid UserId { get; private set; }
    public string LegalName { get; private set; } = null!;
    public string Document { get; private set; } = null!;
    public string ExternalReference { get; private set; } = null!;
    public string? AsaasCustomerId { get; private set; }
    public string Status { get; private set; } = "LOOKUP_PENDING";
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid Version { get; private set; }

    public static BuyerCustomer Register(Guid userId, string name, string document, DateTimeOffset now)
    {
        if (userId == Guid.Empty) throw new DomainException("Buyer identity is required.");
        var profile = new BuyerCustomer { UserId = userId, ExternalReference = $"vaulta_buyer_{userId:N}" };
        profile.Update(name, document, now);
        return profile;
    }
    public void Update(string name, string document, DateTimeOffset now)
    {
        var normalizedDocument = NormalizeDocument(document);
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 200)
            throw new DomainException("Informe o nome completo ou a razão social, com até 200 caracteres.");
        if (LegalName == normalizedName && Document == normalizedDocument)
        {
            if (Status == "REJECTED") throw new DomainException("Confira e corrija os dados de cobrança recusados pelo provedor.");
            return;
        }
        if (SubmittedAt.HasValue && Status != "REJECTED" || AsaasCustomerId is not null)
            throw new ConflictException("Alterar dados de cobrança já enviados exige atendimento.");
        LegalName = normalizedName; Document = normalizedDocument;
        SubmittedAt = null; Status = "LOOKUP_PENDING"; Touch(now);
    }
    public static string NormalizeDocument(string? document)
    {
        if (string.IsNullOrWhiteSpace(document) || document.Length > 24
            || document.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '/' or ' ')))
            throw new DomainException("Informe CPF ou CNPJ válido.");
        var normalized = string.Concat(document.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
        if (normalized.Length is not (11 or 14) || normalized.Length == 11 && !normalized.All(char.IsAsciiDigit)
            || !normalized[^2..].All(char.IsAsciiDigit) || normalized.Distinct().Count() == 1)
            throw new DomainException("Informe CPF ou CNPJ válido.");
        // The provider validates the document; registration is not identity/KYC verification.
        return normalized;
    }
    public void Claim(DateTimeOffset now, Guid version)
    {
        if (SubmittedAt.HasValue || Status != "LOOKUP_PENDING") throw new ConflictException("Customer creation already submitted.");
        SubmittedAt = now; Status = "SUBMITTING"; UpdatedAt = now; Version = version;
    }
    public void Bind(string customerId, string reference, string document, bool notificationsDisabled, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(customerId) || customerId.Length > 200 || reference != ExternalReference
            || NormalizeDocument(document) != Document || !notificationsDisabled
            || AsaasCustomerId is not null && AsaasCustomerId != customerId)
            throw new ConflictException("Cadastro do provedor não corresponde ao comprador.");
        AsaasCustomerId = customerId; Status = "READY"; Touch(now);
    }
    public void Reconcile(DateTimeOffset now) { Status = "RECONCILIATION_REQUIRED"; Touch(now); }
    public void Reject(DateTimeOffset now) { Status = "REJECTED"; Touch(now); }
    private void Touch(DateTimeOffset now) { UpdatedAt = now; Version = Guid.NewGuid(); }
}
