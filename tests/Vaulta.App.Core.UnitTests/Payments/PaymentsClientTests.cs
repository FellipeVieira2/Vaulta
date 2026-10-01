using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vaulta.App.Core.Payments;
using Vaulta.App.Core.UnitTests.TestSupport;
using Vaulta.Payments.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Payments;

public sealed class PaymentsClientTests
{
    [Fact]
    public async Task MissingPrivateCustomerAndPaymentReturnNull()
    {
        var orderId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains(request.RequestUri!.AbsolutePath, new[] { "/api/v1/payments/customer", $"/api/v1/payments/orders/{orderId}" });
            return new(HttpStatusCode.NoContent);
        });
        var client = Client(handler);
        Assert.Null(await client.GetCustomerAsync()); Assert.Null(await client.GetOrderPaymentAsync(orderId));
    }

    [Fact]
    public async Task BillingRegistrationCannotSupplyProviderIdOrAnotherUser()
    {
        var handler = new FakeHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("/api/v1/payments/customer", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(2, body.RootElement.EnumerateObject().Count());
            Assert.Equal("Pagador", body.RootElement.GetProperty("legalName").GetString());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new BuyerCustomerDto("Pagador", "*******4725", "READY")) };
        });
        var result = await Client(handler).RegisterCustomerAsync(new("Pagador", "52998224725"));
        Assert.Equal("*******4725", result.MaskedDocument); Assert.Equal("READY", result.Status);
    }

    [Fact]
    public async Task RestoredPaymentPreservesProviderPixExpiration()
    {
        var orderId = Guid.NewGuid();
        var dto = new PaymentDto(Guid.NewGuid(), orderId, Guid.NewGuid(), Guid.NewGuid(), 100, 100, "BRL", "PIX", "pending",
            "pay_test", "https://sandbox.asaas.com/i/test", "copy-code", null, null, [], DateTimeOffset.UtcNow, null, Guid.NewGuid(), "2026-10-02 23:59:59");
        var handler = new FakeHttpMessageHandler(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(dto) });
        var result = await Client(handler).GetOrderPaymentAsync(orderId);
        Assert.Equal("copy-code", result!.PixQrCode); Assert.Equal(dto.PixExpirationDate, result.PixExpirationDate);
    }

    private static PaymentsClient Client(FakeHttpMessageHandler handler) => new(new HttpClient(handler) { BaseAddress = new("https://api.example.test/") });
}
