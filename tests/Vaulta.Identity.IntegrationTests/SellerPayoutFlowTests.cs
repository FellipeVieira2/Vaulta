using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vaulta.Identity.Contracts;
using Vaulta.Identity.Application;
using Vaulta.Marketplace.Domain;
using Vaulta.Marketplace.Infrastructure;
using Vaulta.Orders.Domain;
using Vaulta.Orders.Infrastructure;
using Vaulta.Payments.Application;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.Payments.Infrastructure;
using Vaulta.Shipping.Domain;
using Vaulta.Shipping.Infrastructure;
using Vaulta.Wallets.Domain;
using Vaulta.Wallets.Infrastructure;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class SellerPayoutFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task PixOwnershipReceiptSettlementAndWebhookControlTheSingleSellerTransfer()
    {
        using var baseClient = fixture.Factory.CreateClient();
        var buyer = await Register(baseClient);
        var seller = await Register(baseClient);
        var reviewer = await Register(baseClient);
        var gateway = new TestPixGateway();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Payouts:IdentityReviewerUserIds:0"] = reviewer.User.Id.ToString() }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPixPayoutGateway>();
                services.AddSingleton<IPixPayoutGateway>(gateway);
            });
        });
        using var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var listing = await CollectionCommerceSeed.Listing(factory.Services, seller.User.Id);
        var order = Order.Create(buyer.User.Id, seller.User.Id, listing.Id, listing.CollectibleItemId, listing.PrintingId, null, "NM", 100m, 8m,
            null, null, null, null, null, null, null, null, now);
        listing.MarkAsSold(order.Id, now);
        var payment = PaymentTransaction.Create(order.Id, buyer.User.Id, seller.User.Id, 100m, "PIX", null, now);
        var providerId = "pay_" + Guid.NewGuid().ToString("N");
        payment.SetCheckoutInfo(providerId, null, null, null);
        payment.Confirm(providerId, 98m, now);
        order.MarkAsPaid(providerId, now);
        order.MarkAsShipped("tracking-test", now);
        var shipment = Shipment.Create(order.Id, seller.User.Id, buyer.User.Id, "CORREIOS", "tracking-test", null, null, null, now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var market = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            market.SellerProfiles.Add(SellerProfile.Enable(seller.User.Id, null, null, null, null, null, now));
            market.Listings.Add(listing); await market.SaveChangesAsync();
            var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>(); orders.Orders.Add(order); await orders.SaveChangesAsync();
            var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>(); payments.PaymentTransactions.Add(payment); await payments.SaveChangesAsync();
            var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>(); shipping.Shipments.Add(shipment); await shipping.SaveChangesAsync();
            var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
            await bus.Publish(new EventEnvelope(Guid.NewGuid(), "identity.user-registered.v1",
                System.Text.Json.JsonSerializer.Serialize(new { UserId = seller.User.Id }), now), default);
            await bus.Publish(new EventEnvelope(Guid.NewGuid(), "payments.payment-confirmed.v1",
                System.Text.Json.JsonSerializer.Serialize(new { PaymentId = payment.Id }), now), default);
            await scope.ServiceProvider.GetRequiredService<SellerPayoutService>().RecordMissing(default);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seller.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/v1/payouts/pix")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/v1/payouts/pix/reviews/{seller.User.Id}/verify",
            new VerifyPixDestinationRequest(Guid.NewGuid(), "identity-evidence-123"))).StatusCode);
        var registered = await client.PutAsJsonAsync("/api/v1/payouts/pix", new RegisterPixDestinationRequest("seller@example.test", "EMAIL"));
        registered.EnsureSuccessStatusCode();
        var destination = await registered.Content.ReadFromJsonAsync<PixDestinationDto>();
        Assert.Equal("PENDING_IDENTITY_REVIEW", destination!.Status);
        Assert.DoesNotContain("seller@example.test", await registered.Content.ReadAsStringAsync());
        var spoofVerified = await client.PutAsJsonAsync("/api/v1/payouts/pix", new { key = "seller@example.test", keyType = "EMAIL", verified = true });
        Assert.Equal(HttpStatusCode.BadRequest, spoofVerified.StatusCode);

        // Seller-reported tracking delivery is not buyer-confirmed order receipt.
        var tracking = await client.PutAsJsonAsync($"/api/v1/shipping/{shipment.Id}/tracking",
            new { status = "delivered", trackingCode = "tracking-test", description = "tracking update", occurredAt = now });
        tracking.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/v1/orders/{order.Id}/deliver", null)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storedOrder = await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.SingleAsync(x => x.Id == order.Id);
            Assert.Equal(OrderRules.ShippedStatus, storedOrder.Status);
            Assert.Null(storedOrder.DeliveredAt);
        }
        var list = await client.GetFromJsonAsync<SellerPayoutPageDto>("/api/v1/payouts?pageSize=10000");
        Assert.Equal(100, list!.PageSize);
        var payout = Assert.Single(list.Items);
        Assert.Equal(92m, payout.AmountBrl); Assert.Equal(8m, payout.PlatformFeeBrl);
        await Process(factory.Services, payout.Id);
        Assert.Equal(0, gateway.Transfers);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reviewer.AccessToken);
        var verified = await client.PostAsJsonAsync($"/api/v1/payouts/pix/reviews/{seller.User.Id}/verify",
            new VerifyPixDestinationRequest(destination.Version, "verified-seller-identity-123"));
        verified.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", buyer.AccessToken);
        Assert.Empty((await client.GetFromJsonAsync<SellerPayoutPageDto>("/api/v1/payouts"))!.Items);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/orders/{order.Id}/deliver", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/orders/{order.Id}/deliver", null)).StatusCode);
        await Process(factory.Services, payout.Id);
        Assert.Equal(0, gateway.Transfers); // Confirmed charge is not yet settled.
        client.DefaultRequestHeaders.Add("asaas-access-token", fixture.WebhookToken);
        (await client.PostAsJsonAsync("/api/v1/webhooks/asaas", new { @event = "PAYMENT_RECEIVED", payment = new { id = providerId, netValue = 98m } })).EnsureSuccessStatusCode();
        await Process(factory.Services, payout.Id);
        await Process(factory.Services, payout.Id);
        Assert.Equal(1, gateway.Transfers);
        Assert.Equal(90m, gateway.LastRequest!.Amount);
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        var rejected = await client.PostAsJsonAsync("/api/v1/webhooks/asaas", new
        {
            id = "evt_wrong_" + Guid.NewGuid().ToString("N"), @event = "TRANSFER_DONE",
            transfer = new { id = gateway.TransferId, status = "DONE", value = 100m, netValue = 99m, transferFee = 1m, externalReference = gateway.LastRequest.ExternalReference }
        });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var webhook = new { id = eventId, @event = "TRANSFER_DONE", transfer = new { id = gateway.TransferId, status = "DONE", value = 90m, netValue = 89m, transferFee = 1m, externalReference = gateway.LastRequest.ExternalReference } };
        (await client.PostAsJsonAsync("/api/v1/webhooks/asaas", webhook)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/webhooks/asaas", webhook)).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seller.AccessToken);
        var completedPage = await client.GetFromJsonAsync<SellerPayoutPageDto>("/api/v1/payouts");
        Assert.Equal("••••test", Assert.Single(completedPage!.Items).MaskedDestinationKey);
        Assert.Equal(2m, completedPage.Items[0].PaymentFeeBrl);
        Assert.Equal(1m, completedPage.Items[0].TransferFeeBrl);
        Assert.Equal(89m, completedPage.Items[0].SellerNetBrl);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var completed = await db.SellerPayouts.SingleAsync(x => x.Id == payout.Id);
            Assert.Equal(PayoutStatus.Done, completed.Status);
            Assert.Equal(reviewer.User.Id, completed.DestinationVerifiedBy);
            Assert.Equal("verified-seller-identity-123", completed.DestinationEvidenceReference);
            Assert.Equal(1, await db.TransferWebhookReceipts.CountAsync(x => x.Id == eventId));
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<WalletsDbContext>().Wallets.CountAsync(x => x.UserId == seller.User.Id));
        }
    }

    [Fact]
    public async Task TwoDatabaseScopesCanClaimOnlyOnceAndKeyChangesInvalidateAnOldClaim()
    {
        var now = DateTimeOffset.UtcNow;
        var seller = Guid.NewGuid();
        var destination = SellerPixDestination.Register(seller, "seller@example.test", "EMAIL", "Seller", "***.202.745-**", now);
        destination.Verify(Guid.NewGuid(), destination.Version, "identity-evidence-123", now);
        var payment = SettledPayment(seller, now);
        var payout = SellerPayout.Create(payment.OrderId, payment.Id, seller, 100, 8, now);
        payout.Evaluate(true, true, 98, destination, false, now);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPayoutStore>(); store.Add(destination); store.Add(payout);
            scope.ServiceProvider.GetRequiredService<IPaymentStore>().Add(payment); await store.Save(default);
        }
        await using var firstScope = fixture.Factory.Services.CreateAsyncScope();
        await using var secondScope = fixture.Factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IPayoutStore>();
        var second = secondScope.ServiceProvider.GetRequiredService<IPayoutStore>();
        var firstPayout = await first.Find(payout.Id, default);
        var secondPayout = await second.Find(payout.Id, default);
        var claims = await Task.WhenAll(first.Claim(firstPayout!, now, default), second.Claim(secondPayout!, now, default));
        Assert.Equal(1, claims.Count(x => x));
        var nextPayment = SettledPayment(seller, now);
        var next = SellerPayout.Create(nextPayment.OrderId, nextPayment.Id, seller, 100, 8, now);
        next.Evaluate(true, true, 98, destination, false, now);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPayoutStore>(); store.Add(next);
            scope.ServiceProvider.GetRequiredService<IPaymentStore>().Add(nextPayment); await store.Save(default);
            var changed = await store.FindDestination(seller, default);
            changed!.Replace("changed@example.test", "EMAIL", "Seller", "***.202.745-**", now);
            await store.Save(default);
            Assert.False(await store.Claim(next, now, default));
        }
    }

    [Fact]
    public async Task ChargebackArrivingAfterEligibilityCheckPreventsTheAtomicTransferClaim()
    {
        var now = DateTimeOffset.UtcNow;
        var seller = Guid.NewGuid(); var payment = SettledPayment(seller, now);
        var destination = SellerPixDestination.Register(seller, "seller@example.test", "EMAIL", "Seller", "***.202.745-**", now);
        destination.Verify(Guid.NewGuid(), destination.Version, "identity-evidence-123", now);
        var payout = SellerPayout.Create(payment.OrderId, payment.Id, seller, 100, 8, now);
        payout.Evaluate(true, true, 98, destination, false, now);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPayoutStore>(); store.Add(destination); store.Add(payout);
            scope.ServiceProvider.GetRequiredService<IPaymentStore>().Add(payment); await store.Save(default);
        }
        await using var claimant = fixture.Factory.Services.CreateAsyncScope();
        var claims = claimant.ServiceProvider.GetRequiredService<IPayoutStore>(); var cached = await claims.Find(payout.Id, default);
        await using (var webhook = fixture.Factory.Services.CreateAsyncScope())
        {
            await webhook.ServiceProvider.GetRequiredService<PaymentCommandHandlers>().Handle(
                new Vaulta.Payments.Application.Commands.ProcessWebhookCommand("PAYMENT_CHARGEBACK_REQUESTED", payment.AsaasPaymentId!, "{}"), default);
        }
        Assert.False(await claims.Claim(cached!, now, default));
    }

    [Fact]
    public async Task ObsoleteWithdrawalDoesNotDebitHistoricalWallet()
    {
        using var client = fixture.Factory.CreateClient(); var auth = await Register(client);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WalletsDbContext>();
            var wallet = Wallet.Create(auth.User.Id, DateTimeOffset.UtcNow); wallet.Credit(100, "historical-test", "test", DateTimeOffset.UtcNow);
            db.Wallets.Add(wallet); await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await client.PostAsJsonAsync("/api/v1/wallets/withdraw", new { amount = 50m });
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        await using var check = fixture.Factory.Services.CreateAsyncScope();
        var stored = await check.ServiceProvider.GetRequiredService<WalletsDbContext>().Wallets.SingleAsync(x => x.UserId == auth.User.Id);
        Assert.Equal(100m, stored.Balance);
    }

    private static Order NewOrder(Guid buyer, Guid seller, DateTimeOffset now) => Order.Create(buyer, seller, Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), null, "NM", 100, 8, null, null, null, null, null, null, null, null, now);
    private static PaymentTransaction SettledPayment(Guid seller, DateTimeOffset now)
    {
        var payment = PaymentTransaction.Create(Guid.NewGuid(), Guid.NewGuid(), seller, 100, "PIX", null, now);
        var id = "pay_" + Guid.NewGuid().ToString("N");
        payment.SetCheckoutInfo(id, null, null, null); payment.Confirm(id, 98, now); payment.RecordSettlement(98, now);
        return payment;
    }
    private static async Task<AuthResponse> Register(HttpClient client)
    {
        var request = new RegisterRequest($"{Guid.NewGuid():N}@example.test", "Secure-Test-Password1!", "u" + Guid.NewGuid().ToString("N")[..20], "User");
        (await client.PostAsJsonAsync("/api/v1/auth/register", request)).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }
    private static async Task Process(IServiceProvider services, Guid id)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SellerPayoutService>().Process(id, true, default);
    }
    private sealed class TestPixGateway : IPixPayoutGateway
    {
        public string TransferId { get; } = "tr_" + Guid.NewGuid().ToString("N");
        public int Transfers { get; private set; }
        public PixTransferRequest? LastRequest { get; private set; }
        public Task<bool> IsPaymentSettled(string id, Guid orderId, decimal amount, decimal minimumNet, CancellationToken ct) => Task.FromResult(true);
        public Task<PixHolder> Lookup(string key, string type, CancellationToken ct) => Task.FromResult(new PixHolder(key, type, "User", "***.202.745-**"));
        public Task<PixTransfer> Transfer(PixTransferRequest request, CancellationToken ct)
        { Transfers++; LastRequest = request; return Task.FromResult(new PixTransfer(TransferId, "PENDING", request.Amount, request.ExternalReference, request.Amount - 1, 1)); }
        public Task<PixTransfer?> FindTransfer(string? id, string reference, DateTimeOffset when, CancellationToken ct) => Task.FromResult<PixTransfer?>(
            LastRequest is null ? null : new(TransferId, "PENDING", LastRequest.Amount, LastRequest.ExternalReference, LastRequest.Amount - 1, 1));
    }
}
