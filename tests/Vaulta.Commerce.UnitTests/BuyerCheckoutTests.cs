using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vaulta.Payments.Application;
using Vaulta.Payments.Application.Commands;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.Payments.Infrastructure;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Commerce.UnitTests;

public sealed class BuyerCheckoutTests
{
    [Fact]
    public async Task CustomerBelongsToAuthenticatedBuyerIsCreatedOnceAndDocumentIsMasked()
    {
        var f = new Fixture(); var userId = Guid.NewGuid();
        var result = await f.Service.Register(userId, new("Buyer", "529.982.247-25"), default);
        Assert.Equal("READY", result.Status); Assert.Equal("••••4725", result.MaskedDocument);
        await f.Service.Register(userId, new("Buyer", "52998224725"), default);
        Assert.Equal(1, f.Gateway.Creates); Assert.Equal("cus_own", await f.Service.Resolve(userId, default));
        Assert.Equal($"vaulta_buyer_{userId:N}", f.Gateway.Found!.ExternalReference);
        Assert.Equal("own@example.test", f.Gateway.Email);
    }

    [Fact]
    public async Task UncertainCustomerPostIsRecoveredByReferenceWithoutAnotherPost()
    {
        var f = new Fixture(); var userId = Guid.NewGuid(); f.Gateway.FailCreate = true;
        var initial = await f.Service.Register(userId, new("Buyer", "52998224725"), default);
        Assert.Equal("RECONCILIATION_REQUIRED", initial.Status);
        var recovered = await f.Service.Register(userId, new("Buyer", "52998224725"), default);
        Assert.Equal("READY", recovered.Status); Assert.Equal(1, f.Gateway.Creates);
    }

    [Fact]
    public async Task EmptyLookupAfterUncertainSubmissionDoesNotCreateCustomerAgain()
    {
        var f = new Fixture(); var userId = Guid.NewGuid(); f.Gateway.FailCreate = true;
        await f.Service.Register(userId, new("Buyer", "52998224725"), default);
        f.Gateway.Found = null;
        await f.Service.Register(userId, new("Buyer", "52998224725"), default);
        Assert.Equal(1, f.Gateway.Creates);
        Assert.Equal("RECONCILIATION_REQUIRED", f.Store.Customer!.Status);
    }

    [Fact]
    public async Task ProviderCustomerWithDifferentDocumentCannotBeBound()
    {
        var f = new Fixture(); var userId = Guid.NewGuid();
        f.Gateway.Found = new("cus_other", $"vaulta_buyer_{userId:N}", "11144477735", true);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.Register(userId, new("Buyer", "52998224725"), default));
        Assert.Null(f.Store.Customer!.AsaasCustomerId); Assert.Equal(0, f.Gateway.Creates);
    }

    [Fact]
    public async Task CustomerRejectionAllowsCorrectionButNotRepeatingUnchangedRejectedData()
    {
        var f = new Fixture(); var userId = Guid.NewGuid(); f.Gateway.Reject = true;
        await Assert.ThrowsAsync<DomainException>(() => f.Service.Register(userId, new("Buyer", "52998224725"), default));
        await Assert.ThrowsAsync<DomainException>(() => f.Service.Register(userId, new("Buyer", "52998224725"), default));
        f.Gateway.Reject = false;
        Assert.Equal("READY", (await f.Service.Register(userId, new("Corrected Buyer", "52998224725"), default)).Status);
        Assert.Equal(2, f.Gateway.Creates);
    }

    [Fact]
    public async Task ForeignCustomerIdCannotCreatePayment()
    {
        var orders = new OrderStore(); var payments = new PaymentStore(); var gateway = new Gateway();
        var handler = new PaymentCommandHandlers(payments, gateway, orders, new TestClock(), new BuyerCustomers());
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new InitiatePaymentCommand(orders.Order.BuyerId,
            new(orders.Order.Id, "PIX", "cus_foreign")), default));
        Assert.Equal(0, gateway.Charges); Assert.Null(payments.Transaction);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChargeWithMissingResponseIsQueriedAndNeverCreatedAgain(bool found)
    {
        var orders = new OrderStore(); var payments = new PaymentStore(); var gateway = new Gateway();
        payments.Add(PaymentTransaction.Create(orders.Order.Id, orders.Order.BuyerId, orders.Order.SellerId, 100, "PIX", "cus_test", new TestClock().UtcNow));
        if (found) gateway.Recovered = new("pay_recovered", "https://sandbox.asaas.com/i/test", null, null, "PENDING");
        var handler = new PaymentCommandHandlers(payments, gateway, orders, new TestClock(), new BuyerCustomers());
        var action = () => handler.Handle(new InitiatePaymentCommand(orders.Order.BuyerId, new(orders.Order.Id, "PIX", null)), default);
        if (found) Assert.Equal("pay_recovered", (await action()).AsaasPaymentId);
        else await Assert.ThrowsAsync<ConflictException>(action);
        Assert.Equal(0, gateway.Charges);
    }

    [Fact]
    public async Task CustomerGatewayDisablesNotificationsAndKeepsProviderIdOutOfClientInput()
    {
        var reference = "vaulta_buyer_test";
        var handler = new HttpHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("/v3/customers", request.RequestUri!.AbsolutePath);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.True(root.GetProperty("notificationDisabled").GetBoolean());
            Assert.Equal(reference, root.GetProperty("externalReference").GetString());
            Assert.False(root.TryGetProperty("id", out _));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "cus_test", cpfCnpj = "52998224725",
                externalReference = reference, notificationDisabled = true, deleted = false }) };
        });
        var http = new HttpClient(handler) { BaseAddress = new("https://provider.example.test") }; http.DefaultRequestHeaders.Add("access_token", "test");
        var result = await new AsaasBuyerCustomerGateway(http).Create("Buyer", "52998224725", "own@example.test", reference, default);
        Assert.Equal("cus_test", result.Id);
    }

    [Fact]
    public async Task PixRetrievalFailureKeepsPersistedChargeAndRetryDoesNotChargeAgain()
    {
        var orders = new OrderStore(); var payments = new PaymentStore();
        var gateway = new Gateway { FailPix = true, BeforePixRead = () =>
        { Assert.Equal("pay_test", payments.Transaction!.AsaasPaymentId); Assert.True(payments.Saves >= 2); } };
        var handler = new PaymentCommandHandlers(payments, gateway, orders, new TestClock(), new BuyerCustomers());
        var command = new InitiatePaymentCommand(orders.Order.BuyerId, new(orders.Order.Id, "PIX", null));
        var first = await handler.Handle(command, default);
        Assert.Equal("pay_test", first.AsaasPaymentId); Assert.Null(first.PixQrCode);
        gateway.FailPix = false;
        var second = await handler.Handle(command, default);
        Assert.Equal(first.Id, second.Id); Assert.Equal("pix-copy-paste-test", second.PixQrCode); Assert.Equal(1, gateway.Charges);
    }

    [Fact]
    public async Task RefreshReplacesChangedPixAndFailedRefreshDoesNotOfferOldCode()
    {
        var orders = new OrderStore(); var payments = new PaymentStore(); var gateway = new Gateway();
        var handler = new PaymentCommandHandlers(payments, gateway, orders, new TestClock(), new BuyerCustomers());
        var command = new InitiatePaymentCommand(orders.Order.BuyerId, new(orders.Order.Id, "PIX", null));
        var first = await handler.Handle(command, default);
        Assert.Equal("2026-10-02 23:59:59", first.PixExpirationDate);
        gateway.PixPayload = "new-code"; gateway.PixExpiration = "2026-10-03 23:59:59";
        var updated = await handler.Handle(command, default);
        Assert.Equal(first.Id, updated.Id); Assert.Equal("new-code", updated.PixQrCode);
        Assert.Equal(gateway.PixExpiration, updated.PixExpirationDate); Assert.NotEqual(first.Version, updated.Version);
        gateway.FailPix = true;
        var unavailable = await handler.Handle(command, default);
        Assert.Null(unavailable.PixQrCode); Assert.Null(unavailable.PixExpirationDate);
        Assert.Equal(first.CheckoutUrl, unavailable.CheckoutUrl); Assert.Equal(first.AsaasPaymentId, unavailable.AsaasPaymentId);
        Assert.Equal(1, gateway.Charges);
    }

    private sealed class Fixture
    {
        public Store Store { get; } = new(); public CustomerGateway Gateway { get; } = new();
        public BuyerCustomerService Service { get; }
        public Fixture() { Service = new(Store, Gateway, new Identity(), new TestClock()); }
    }
    private sealed class Store : IBuyerCustomerStore
    {
        public BuyerCustomer? Customer { get; private set; }
        public Task<BuyerCustomer?> Find(Guid userId, CancellationToken ct) => Task.FromResult(Customer?.UserId == userId ? Customer : null);
        public void Add(BuyerCustomer customer) => Customer = customer;
        public Task Save(CancellationToken ct) => Task.CompletedTask;
        public Task<bool> Claim(BuyerCustomer customer, DateTimeOffset now, CancellationToken ct)
        { customer.Claim(now, Guid.NewGuid()); return Task.FromResult(true); }
    }
    private sealed class Identity : IBuyerCustomerIdentity
    { public Task<string> GetEmail(Guid userId, CancellationToken ct) => Task.FromResult("own@example.test"); }
    private sealed class CustomerGateway : IBuyerCustomerGateway
    {
        public int Creates { get; private set; } public ProviderCustomer? Found { get; set; }
        public bool FailCreate { get; set; } public bool Reject { get; set; } public string? Email { get; private set; }
        public Task<ProviderCustomer?> Find(string reference, CancellationToken ct) => Task.FromResult(Found);
        public Task<ProviderCustomer> Create(string name, string document, string email, string reference, CancellationToken ct)
        {
            Creates++; Email = email;
            if (Reject) return Task.FromException<ProviderCustomer>(new HttpRequestException("Rejected", null, HttpStatusCode.BadRequest));
            Found = new("cus_own", reference, document, true);
            return FailCreate ? Task.FromException<ProviderCustomer>(new HttpRequestException("Unknown outcome")) : Task.FromResult(Found);
        }
    }
    private sealed class HttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request); }
}
