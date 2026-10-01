using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.Identity.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.Marketplace.Infrastructure;
using Vaulta.Orders.Contracts;
using Vaulta.Payments.Application;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.Payments.Infrastructure;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class BuyerCheckoutFlowTests(ApiFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedCustomerChargeRecoveryAndPixRetrievalWorkWithoutDuplicatePosts(bool failChargeResponse)
    {
        using var seedClient = fixture.Factory.CreateClient();
        var buyer = await Register(seedClient); var seller = await Register(seedClient); var stranger = await Register(seedClient);
        var provider = new ProviderHandler(failChargeResponse);
        var http = new HttpClient(provider) { BaseAddress = new("https://provider.example.test") };
        http.DefaultRequestHeaders.Add("access_token", "test");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Asaas:ApiKey"] = "test" }).Build();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBuyerCustomerGateway>(); services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IBuyerCustomerGateway>(new AsaasBuyerCustomerGateway(http));
            services.AddSingleton<IPaymentGateway>(new AsaasPaymentGateway(http, config, NullLogger<AsaasPaymentGateway>.Instance));
        }));
        using var api = factory.CreateClient();
        api.DefaultRequestHeaders.Authorization = new("Bearer", buyer.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await api.GetAsync("/api/v1/payments/customer")).StatusCode);
        var registration = await api.PutAsJsonAsync("/api/v1/payments/customer", new RegisterBuyerCustomerRequest("Buyer", "52998224725"));
        registration.EnsureSuccessStatusCode();
        Assert.DoesNotContain("52998224725", await registration.Content.ReadAsStringAsync());
        Assert.Equal("READY", (await registration.Content.ReadFromJsonAsync<BuyerCustomerDto>())!.Status);
        Assert.Equal($"vaulta_buyer_{buyer.User.Id:N}", provider.CustomerReference);
        (await api.PutAsJsonAsync("/api/v1/payments/customer", new RegisterBuyerCustomerRequest("Buyer", "52998224725"))).EnsureSuccessStatusCode();
        Assert.Equal(1, provider.CustomerPosts);
        var spoofed = await api.PutAsJsonAsync("/api/v1/payments/customer", new { legalName = "Buyer", document = "52998224725", asaasCustomerId = "cus_foreign" });
        Assert.Equal(HttpStatusCode.BadRequest, spoofed.StatusCode);
        var listing = await CollectionCommerceSeed.Listing(factory.Services, seller.User.Id);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var market = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            market.SellerProfiles.Add(SellerProfile.Enable(seller.User.Id, null, null, null, null, null, DateTimeOffset.UtcNow));
            market.Listings.Add(listing); await market.SaveChangesAsync();
        }
        var created = await api.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(listing.Id, null, null, null, null, null, null, null, null));
        created.EnsureSuccessStatusCode(); var order = (await created.Content.ReadFromJsonAsync<OrderDto>())!;
        var foreign = await api.PostAsJsonAsync("/api/v1/payments", new CreatePaymentRequest(order.Id, "PIX", "cus_foreign"));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode); Assert.Equal(0, provider.ChargePosts);
        var first = await api.PostAsJsonAsync("/api/v1/payments", new CreatePaymentRequest(order.Id, "PIX", null));
        if (failChargeResponse) Assert.False(first.IsSuccessStatusCode);
        else
        {
            first.EnsureSuccessStatusCode(); var initial = await first.Content.ReadFromJsonAsync<PaymentDto>();
            Assert.Equal(provider.ChargeId, initial!.AsaasPaymentId); Assert.Null(initial.PixQrCode);
            Assert.Equal("https://sandbox.asaas.com/i/test", initial.CheckoutUrl);
        }
        var retried = await api.PostAsJsonAsync("/api/v1/payments", new CreatePaymentRequest(order.Id, "PIX", null));
        retried.EnsureSuccessStatusCode(); var payment = (await retried.Content.ReadFromJsonAsync<PaymentDto>())!;
        Assert.Equal(1, provider.ChargePosts); Assert.Equal("000201-test-pix", payment.PixQrCode); Assert.Equal(100m, payment.Amount);
        Assert.Equal("2026-10-02 23:59:59", payment.PixExpirationDate);
        if (failChargeResponse) Assert.Equal(1, provider.ChargeSearches);
        var persisted = await api.GetFromJsonAsync<PaymentDto>($"/api/v1/payments/orders/{order.Id}");
        Assert.Equal(payment.Id, persisted!.Id);
        Assert.Equal(payment.PixExpirationDate, persisted.PixExpirationDate);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync($"/api/v1/payments/orders/{order.Id}")).StatusCode);
        api.DefaultRequestHeaders.Authorization = new("Bearer", stranger.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/v1/payments/orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.GetAsync("/api/v1/payments/customer")).StatusCode);
        await using var inspect = factory.Services.CreateAsyncScope();
        var row = await inspect.ServiceProvider.GetRequiredService<PaymentsDbContext>().PaymentTransactions.SingleAsync(x => x.Id == payment.Id);
        Assert.Equal(provider.CustomerId, row.AsaasCustomerId);
        Assert.Equal(payment.PixExpirationDate, row.PixExpirationDate);
    }

    [Fact]
    public async Task BuyerCustomerClaimIsAtomicAcrossDatabaseContexts()
    {
        var customer = BuyerCustomer.Register(Guid.NewGuid(), "Buyer", "52998224725", DateTimeOffset.UtcNow);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>(); db.BuyerCustomers.Add(customer); await db.SaveChangesAsync();
        }
        await using var first = fixture.Factory.Services.CreateAsyncScope(); await using var second = fixture.Factory.Services.CreateAsyncScope();
        var aStore = first.ServiceProvider.GetRequiredService<IBuyerCustomerStore>(); var bStore = second.ServiceProvider.GetRequiredService<IBuyerCustomerStore>();
        var a = await aStore.Find(customer.UserId, default); var b = await bStore.Find(customer.UserId, default);
        var results = await Task.WhenAll(aStore.Claim(a!, DateTimeOffset.UtcNow, default), bStore.Claim(b!, DateTimeOffset.UtcNow, default));
        Assert.Single(results, x => x);
    }

    private static async Task<AuthResponse> Register(HttpClient api)
    {
        var request = new RegisterRequest($"{Guid.NewGuid():N}@example.test", "Secure-Test-Password1!", "u" + Guid.NewGuid().ToString("N")[..20], "User");
        (await api.PostAsJsonAsync("/api/v1/auth/register", request)).EnsureSuccessStatusCode();
        var response = await api.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private sealed class ProviderHandler(bool failChargeResponse) : HttpMessageHandler
    {
        public string CustomerId { get; } = "cus_" + Guid.NewGuid().ToString("N");
        public string ChargeId { get; } = "pay_" + Guid.NewGuid().ToString("N");
        public string? CustomerReference { get; private set; }
        public int CustomerPosts { get; private set; } public int ChargePosts { get; private set; }
        public int ChargeSearches { get; private set; } private int _pixReads;
        private string? _orderReference;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v3/customers")
            {
                if (request.Method == HttpMethod.Post)
                {
                    CustomerPosts++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    Assert.True(body.RootElement.GetProperty("notificationDisabled").GetBoolean());
                    CustomerReference = body.RootElement.GetProperty("externalReference").GetString();
                    return Json(Customer());
                }
                return Json(new { hasMore = false, data = CustomerPosts == 0 ? Array.Empty<object>() : new[] { Customer() } });
            }
            if (path.EndsWith("/pixQrCode", StringComparison.Ordinal))
            {
                _pixReads++;
                if (!failChargeResponse && _pixReads == 1) return new(HttpStatusCode.InternalServerError);
                return Json(new { payload = "000201-test-pix", expirationDate = "2026-10-02 23:59:59" });
            }
            Assert.Equal("/v3/payments", path);
            if (request.Method == HttpMethod.Post)
            {
                ChargePosts++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(CustomerId, body.RootElement.GetProperty("customer").GetString());
                Assert.Equal(100m, body.RootElement.GetProperty("value").GetDecimal());
                Assert.False(body.RootElement.TryGetProperty("split", out _));
                _orderReference = body.RootElement.GetProperty("externalReference").GetString();
                if (failChargeResponse) return new(HttpStatusCode.InternalServerError);
                return Json(Charge());
            }
            ChargeSearches++; return Json(new { hasMore = false, data = new[] { Charge() } });
        }
        private object Customer() => new { id = CustomerId, cpfCnpj = "52998224725", externalReference = CustomerReference, notificationDisabled = true, deleted = false };
        private object Charge() => new { id = ChargeId, customer = CustomerId, externalReference = _orderReference, value = 100m, billingType = "PIX", status = "PENDING", invoiceUrl = "https://sandbox.asaas.com/i/test", deleted = false };
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
