using Microsoft.EntityFrameworkCore;
using Vaulta.Identity.Application;
using Vaulta.Payments.Application;
using Vaulta.Payments.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Infrastructure;

public sealed class BuyerCustomerStore(PaymentsDbContext db) : IBuyerCustomerStore
{
    public Task<BuyerCustomer?> Find(Guid userId, CancellationToken ct) => db.BuyerCustomers.SingleOrDefaultAsync(x => x.UserId == userId, ct);
    public void Add(BuyerCustomer customer) => db.BuyerCustomers.Add(customer);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
    public async Task<bool> Claim(BuyerCustomer customer, DateTimeOffset now, CancellationToken ct)
    {
        var version = Guid.NewGuid();
        var count = await db.BuyerCustomers.Where(x => x.UserId == customer.UserId && x.Version == customer.Version
                && x.Status == "LOOKUP_PENDING" && x.SubmittedAt == null && x.AsaasCustomerId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SUBMITTING").SetProperty(x => x.SubmittedAt, now)
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.Version, version), ct);
        if (count != 1) return false;
        customer.Claim(now, version);
        db.Entry(customer).OriginalValues.SetValues(customer); db.Entry(customer).State = EntityState.Unchanged;
        return true;
    }
}
internal sealed class BuyerCustomerIdentity(IIdentityStore users) : IBuyerCustomerIdentity
{
    public async Task<string> GetEmail(Guid userId, CancellationToken ct)
    {
        var user = await users.FindUser(userId, ct);
        if (user?.CanLogin != true) throw new ForbiddenException("Comprador indisponível.");
        return user.Email;
    }
}
