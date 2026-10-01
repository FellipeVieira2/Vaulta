using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.Payments.Application;
using Vaulta.Payments.Infrastructure;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Commerce.UnitTests;

public sealed class AsaasCheckoutGatewayTests
{
    [Fact]
    public async Task ChargeUsesOwnedCustomerAndInvoiceUrlWhilePixComesFromSeparateEndpoint()
    {
        var orderId = Guid.NewGuid().ToString(); var posts = 0; var reads = 0;
        var handler = new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                reads++; Assert.Equal("/v3/payments/pay_test/pixQrCode", request.RequestUri!.AbsolutePath);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { payload = "000201-pix-test", encodedImage = "test", expirationDate = "2026-10-02" }) };
            }
            posts++; Assert.Equal("/v3/payments", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("cus_owned", body.RootElement.GetProperty("customer").GetString());
            Assert.False(body.RootElement.TryGetProperty("split", out _));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "pay_test", customer = "cus_owned", externalReference = orderId,
                value = 100, billingType = "PIX", status = "PENDING", invoiceUrl = "https://sandbox.asaas.com/i/test", deleted = false }) };
        });
        var gateway = Gateway(handler);
        var result = await gateway.CreatePaymentAsync(new(100, "PIX", "cus_owned", orderId, "Vaulta Order", []), default);
        Assert.Equal("https://sandbox.asaas.com/i/test", result.CheckoutUrl); Assert.Null(result.PixQrCode);
        Assert.Equal(0, reads);
        var pix = await gateway.GetPixPayloadAsync(result.AsaasPaymentId, default);
        Assert.Equal("000201-pix-test", pix.Payload); Assert.Equal("2026-10-02", pix.ExpirationDate);
        Assert.Equal(1, posts); Assert.Equal(1, reads);
    }

    [Theory]
    [InlineData("cus_foreign", 100)]
    [InlineData("cus_owned", 108)]
    public async Task RecoveryRejectsProviderRecordsWithWrongBuyerOrAmount(string customer, int amount)
    {
        var orderId = Guid.NewGuid().ToString();
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { hasMore = false,
                data = new[] { new { id = "pay_test", customer, externalReference = orderId, value = amount, billingType = "PIX", status = "PENDING", deleted = false } } }) });
        });
        await Assert.ThrowsAsync<ConflictException>(() => Gateway(handler).FindPaymentAsync(new(100, "PIX", "cus_owned", orderId, "Order", []), default));
    }

    private static AsaasPaymentGateway Gateway(Handler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new("https://provider.example.test") };
        http.DefaultRequestHeaders.Add("access_token", "test");
        return new(http, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Asaas:ApiKey"] = "test" }).Build(), NullLogger<AsaasPaymentGateway>.Instance);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request); }
}
