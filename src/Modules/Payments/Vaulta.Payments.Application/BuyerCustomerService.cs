using System.Net;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Application;

public sealed class BuyerCustomerService(IBuyerCustomerStore store, IBuyerCustomerGateway gateway,
    IBuyerCustomerIdentity identity, IClock clock) : IBuyerCustomerAccess
{
    public async Task<BuyerCustomerDto?> Get(Guid userId, CancellationToken ct)
    {
        var customer = await store.Find(userId, ct);
        return customer is null ? null : Map(customer);
    }
    public async Task<BuyerCustomerDto> Register(Guid userId, RegisterBuyerCustomerRequest request, CancellationToken ct)
    {
        var email = await identity.GetEmail(userId, ct);
        var customer = await store.Find(userId, ct);
        if (customer is null) { customer = BuyerCustomer.Register(userId, request.LegalName, request.Document, clock.UtcNow); store.Add(customer); }
        else customer.Update(request.LegalName, request.Document, clock.UtcNow);
        await store.Save(ct);
        await Ensure(customer, email, ct);
        return Map(customer);
    }
    public async Task<string> Resolve(Guid userId, CancellationToken ct)
    {
        var email = await identity.GetEmail(userId, ct);
        var customer = await store.Find(userId, ct) ?? throw new ConflictException("Cadastre seus dados de cobrança antes de pagar.");
        await Ensure(customer, email, ct);
        if (customer.Status != "READY" || customer.AsaasCustomerId is null)
            throw new ConflictException("Cadastro de cobrança em conciliação. Aguarde ou procure atendimento.");
        return customer.AsaasCustomerId;
    }
    private async Task Ensure(BuyerCustomer customer, string email, CancellationToken ct)
    {
        var found = await gateway.Find(customer.ExternalReference, ct);
        if (found is not null)
        {
            customer.Bind(found.Id, found.ExternalReference, found.Document, found.NotificationsDisabled, clock.UtcNow);
            await store.Save(ct); return;
        }
        if (customer.SubmittedAt.HasValue || customer.AsaasCustomerId is not null)
        {
            customer.Reconcile(clock.UtcNow); await store.Save(ct); return;
        }
        if (!await store.Claim(customer, clock.UtcNow, ct)) throw new ConflictException("Cadastro de cobrança em processamento.");
        try
        {
            var created = await gateway.Create(customer.LegalName, customer.Document, email, customer.ExternalReference, ct);
            customer.Bind(created.Id, created.ExternalReference, created.Document, created.NotificationsDisabled, clock.UtcNow);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            customer.Reject(clock.UtcNow); await store.Save(ct);
            throw new DomainException("O provedor recusou os dados de cobrança. Confira nome e CPF/CNPJ.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DomainException or ConflictException)
        { customer.Reconcile(clock.UtcNow); }
        await store.Save(ct);
    }
    private static BuyerCustomerDto Map(BuyerCustomer c) => new(c.LegalName, "••••" + c.Document[^4..], c.Status);
}
