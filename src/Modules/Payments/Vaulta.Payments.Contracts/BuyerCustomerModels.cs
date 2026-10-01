namespace Vaulta.Payments.Contracts;

public sealed record RegisterBuyerCustomerRequest(string LegalName, string Document);
public sealed record BuyerCustomerDto(string LegalName, string MaskedDocument, string Status);
