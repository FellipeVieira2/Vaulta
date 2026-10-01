using Vaulta.Payments.Domain;

namespace Vaulta.Payments.Application;

public interface IBuyerCustomerStore
{
    Task<BuyerCustomer?> Find(Guid userId, CancellationToken ct);
    void Add(BuyerCustomer customer);
    Task<bool> Claim(BuyerCustomer customer, DateTimeOffset now, CancellationToken ct);
    Task Save(CancellationToken ct);
}
public interface IBuyerCustomerIdentity
{
    Task<string> GetEmail(Guid userId, CancellationToken ct);
}
public interface IBuyerCustomerGateway
{
    Task<ProviderCustomer?> Find(string externalReference, CancellationToken ct);
    Task<ProviderCustomer> Create(string name, string document, string email, string externalReference, CancellationToken ct);
}
public interface IBuyerCustomerAccess
{
    Task<string> Resolve(Guid userId, CancellationToken ct);
}
public sealed record ProviderCustomer(string Id, string ExternalReference, string Document, bool NotificationsDisabled);
