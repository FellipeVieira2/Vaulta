using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vaulta.Identity.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.Marketplace.Infrastructure;
using Vaulta.Orders.Application;
using Vaulta.Orders.Contracts;
using Vaulta.Orders.Domain;
using Vaulta.Orders.Infrastructure;
using Vaulta.Payments.Application;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.Payments.Infrastructure;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class PaymentRefundFlowTests(ApiFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PaidCancellationIsPrivateIdempotentAndWaitsForFullProviderConfirmation(bool buyerCancels)
    {
        using var client = fixture.Factory.CreateClient();
        var buyer = await Register(client); var seller = await Register(client); var stranger = await Register(client);
        var gateway = new Gateway();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRefundGateway>(); services.AddSingleton<IRefundGateway>(gateway);
        }));
        using var api = factory.CreateClient();
        var listing = await CollectionCommerceSeed.Listing(factory.Services, seller.User.Id);
        var (order, payment) = NewPayment(buyer.User.Id, seller.User.Id, listing);
        listing.MarkAsSold(order.Id, DateTimeOffset.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var market = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            market.SellerProfiles.Add(SellerProfile.Enable(seller.User.Id, null, null, null, null, null, DateTimeOffset.UtcNow));
            market.Listings.Add(listing); await market.SaveChangesAsync();
        }
        await Seed(factory.Services, order, payment);
        api.DefaultRequestHeaders.Authorization = new("Bearer", stranger.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", new CancelOrderRequest("Cancelar"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync($"/api/v1/orders/{order.Id}/refund")).StatusCode);
        api.DefaultRequestHeaders.Authorization = new("Bearer", buyerCancels ? buyer.AccessToken : seller.AccessToken);
        Assert.Equal(HttpStatusCode.Accepted, (await api.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", new CancelOrderRequest("Antes do envio"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await api.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", new CancelOrderRequest("Repetição"))).StatusCode);
        var state = await api.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}");
        Assert.Equal(OrderRules.RefundPendingStatus, state!.Status);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync($"/api/v1/orders/{order.Id}/ship", new MarkShippedRequest("tracking"))).StatusCode);
        await Process(factory.Services, payment.Id, false);
        Assert.Equal(0, gateway.Requests);
        await Process(factory.Services, payment.Id, true);
        await Process(factory.Services, payment.Id, true);
        Assert.Equal(1, gateway.Requests); Assert.Equal(100m, gateway.Amount);
        Assert.Equal("PROCESSING", (await api.GetFromJsonAsync<OrderRefundDto>($"/api/v1/orders/{order.Id}/refund"))!.Status);
        api.DefaultRequestHeaders.Add("asaas-access-token", fixture.WebhookToken);
        var refund = new { @event = "PAYMENT_REFUNDED", payment = new { id = payment.AsaasPaymentId, value = 100m,
            status = "REFUNDED", refunds = new[] { new { status = "DONE", value = 100m } } } };
        (await api.PostAsJsonAsync("/api/v1/webhooks/asaas", refund)).EnsureSuccessStatusCode();
        (await api.PostAsJsonAsync("/api/v1/webhooks/asaas", refund)).EnsureSuccessStatusCode();
        state = await api.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}");
        Assert.Equal(OrderRules.RefundedStatus, state!.Status);
        Assert.Equal("DONE", (await api.GetFromJsonAsync<OrderRefundDto>($"/api/v1/orders/{order.Id}/refund"))!.Status);
        await Process(factory.Services, payment.Id, true);
        Assert.Equal(1, gateway.Requests);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var finalOrder = await scope.ServiceProvider.GetRequiredService<IOrderStore>().FindOrder(order.Id, default);
            await scope.ServiceProvider.GetRequiredService<IOrderMarketplace>().ReleaseListing(finalOrder!, default);
        }
        api.DefaultRequestHeaders.Authorization = new("Bearer", stranger.AccessToken);
        var purchase = await api.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(listing.Id, null, null, null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, purchase.StatusCode);
        var nextOrder = await purchase.Content.ReadFromJsonAsync<OrderDto>();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var finalOrder = await scope.ServiceProvider.GetRequiredService<IOrderStore>().FindOrder(order.Id, default);
            await scope.ServiceProvider.GetRequiredService<IOrderMarketplace>().ReleaseListing(finalOrder!, default);
            var current = await scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>().Listings.SingleAsync(x => x.Id == listing.Id);
            Assert.Equal(MarketplaceRules.SoldStatus, current.Status);
            Assert.Equal(nextOrder!.Id, current.SoldToOrderId);
        }
        // A different paid order already shipped has no self-service refund path.
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        var (shipped, shippedPayment) = NewPayment(buyer.User.Id, seller.User.Id);
        shipped.MarkAsShipped("tracking", DateTimeOffset.UtcNow);
        await Seed(factory.Services, shipped, shippedPayment);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsJsonAsync($"/api/v1/orders/{shipped.Id}/cancel", new CancelOrderRequest("Depois do envio"))).StatusCode);
    }

    [Fact]
    public async Task AtomicRefundClaimAllowsOnlyOneSubmissionAcrossDatabaseContexts()
    {
        var (order, payment) = NewPayment(Guid.NewGuid(), Guid.NewGuid());
        payment.RequestRefund(order.BuyerId, "Cancelar", DateTimeOffset.UtcNow);
        order.RequestRefund("Cancelar", DateTimeOffset.UtcNow);
        await Seed(fixture.Factory.Services, order, payment);
        await using var first = fixture.Factory.Services.CreateAsyncScope();
        await using var second = fixture.Factory.Services.CreateAsyncScope();
        var a = await first.ServiceProvider.GetRequiredService<IPaymentStore>().FindById(payment.Id, default);
        var b = await second.ServiceProvider.GetRequiredService<IPaymentStore>().FindById(payment.Id, default);
        var outcomes = await Task.WhenAll(first.ServiceProvider.GetRequiredService<IRefundStore>().Claim(a!, DateTimeOffset.UtcNow, default),
            second.ServiceProvider.GetRequiredService<IRefundStore>().Claim(b!, DateTimeOffset.UtcNow, default));
        Assert.Single(outcomes, x => x);
        await using var inspect = fixture.Factory.Services.CreateAsyncScope();
        var stored = await inspect.ServiceProvider.GetRequiredService<PaymentsDbContext>().PaymentTransactions.SingleAsync(x => x.Id == payment.Id);
        Assert.NotNull(stored.RefundSubmittedAt); Assert.Equal("SUBMITTING", stored.RefundStatus);
    }

    [Fact]
    public async Task LatePaymentForCancelledOrderQueuesRefundWithoutDisplacingReplacementPurchase()
    {
        using var api = fixture.Factory.CreateClient();
        var buyer = await Register(api); var seller = await Register(api); var nextBuyer = await Register(api);
        var listing = await CollectionCommerceSeed.Listing(fixture.Factory.Services, seller.User.Id);
        var (order, payment) = NewPayment(buyer.User.Id, seller.User.Id, listing, pending: true);
        listing.MarkAsSold(order.Id, DateTimeOffset.UtcNow);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var market = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            market.SellerProfiles.Add(SellerProfile.Enable(seller.User.Id, null, null, null, null, null, DateTimeOffset.UtcNow));
            market.Listings.Add(listing); await market.SaveChangesAsync();
        }
        await Seed(fixture.Factory.Services, order, payment);
        api.DefaultRequestHeaders.Authorization = new("Bearer", buyer.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await api.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", new CancelOrderRequest("Cancelar antes da confirmação"))).StatusCode);
        api.DefaultRequestHeaders.Authorization = new("Bearer", nextBuyer.AccessToken);
        var purchase = await api.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(listing.Id, null, null, null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, purchase.StatusCode);
        var nextOrder = await purchase.Content.ReadFromJsonAsync<OrderDto>();
        api.DefaultRequestHeaders.Add("asaas-access-token", fixture.WebhookToken);
        (await api.PostAsJsonAsync("/api/v1/webhooks/asaas", new { @event = "PAYMENT_RECEIVED", payment = new { id = payment.AsaasPaymentId,
            value = 100, netValue = 98, externalReference = order.Id.ToString() } })).EnsureSuccessStatusCode();
        api.DefaultRequestHeaders.Authorization = new("Bearer", buyer.AccessToken);
        Assert.Equal("REQUESTED", (await api.GetFromJsonAsync<OrderRefundDto>($"/api/v1/orders/{order.Id}/refund"))!.Status);
        (await api.PostAsJsonAsync("/api/v1/webhooks/asaas", new { @event = "PAYMENT_REFUNDED", payment = new { id = payment.AsaasPaymentId,
            value = 100, status = "REFUNDED", refunds = new[] { new { status = "DONE", value = 100 } } } })).EnsureSuccessStatusCode();
        await using var inspect = fixture.Factory.Services.CreateAsyncScope();
        var stored = await inspect.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.SingleAsync(x => x.Id == order.Id);
        Assert.Equal(OrderRules.RefundedStatus, stored.Status);
        await inspect.ServiceProvider.GetRequiredService<IOrderMarketplace>().ReleaseListing(stored, default);
        var current = await inspect.ServiceProvider.GetRequiredService<MarketplaceDbContext>().Listings.SingleAsync(x => x.Id == listing.Id);
        Assert.Equal(MarketplaceRules.SoldStatus, current.Status); Assert.Equal(nextOrder!.Id, current.SoldToOrderId);
    }

    private static (Order, PaymentTransaction) NewPayment(Guid buyer, Guid seller, Listing? listing = null, bool pending = false)
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(buyer, seller, listing?.Id ?? Guid.NewGuid(), listing?.CollectibleItemId ?? Guid.NewGuid(), listing?.PrintingId ?? Guid.NewGuid(), null, "NM", 100, 8,
            null, null, null, null, null, null, null, null, now);
        var payment = PaymentTransaction.Create(order.Id, buyer, seller, 100, "PIX", null, now);
        var id = "pay_" + Guid.NewGuid().ToString("N");
        payment.SetCheckoutInfo(id, null, null, null);
        if (!pending)
        {
            payment.Confirm(id, 98, now); payment.RecordSettlement(98, now); order.MarkAsPaid(id, now);
        }
        return (order, payment);
    }
    private static async Task Seed(IServiceProvider services, Order order, PaymentTransaction payment)
    {
        await using var scope = services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>(); orders.Orders.Add(order); await orders.SaveChangesAsync();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>(); payments.PaymentTransactions.Add(payment); await payments.SaveChangesAsync();
    }
    private static async Task Process(IServiceProvider services, Guid id, bool enabled)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentRefundService>().Process(id, enabled, default);
    }
    private static async Task<AuthResponse> Register(HttpClient client)
    {
        var request = new RegisterRequest("refund-" + Guid.NewGuid().ToString("N") + "@example.test", "Secure-Test-Password1!", "u" + Guid.NewGuid().ToString("N")[..20], "User");
        (await client.PostAsJsonAsync("/api/v1/auth/register", request)).EnsureSuccessStatusCode();
        var result = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password));
        result.EnsureSuccessStatusCode(); return (await result.Content.ReadFromJsonAsync<AuthResponse>())!;
    }
    private sealed class Gateway : IRefundGateway
    {
        public int Requests { get; private set; }
        public decimal Amount { get; private set; }
        public Task<ProviderRefund> Get(string id, Guid order, decimal amount, CancellationToken ct) =>
            Task.FromResult(Requests > 0 ? new ProviderRefund(false, 0, amount) : new ProviderRefund(true, 0, 0));
        public Task<ProviderRefund> Request(string id, Guid order, decimal amount, string billing, string reason, CancellationToken ct)
        { Requests++; Amount = amount; return Task.FromResult(new ProviderRefund(false, 0, amount)); }
    }
}
