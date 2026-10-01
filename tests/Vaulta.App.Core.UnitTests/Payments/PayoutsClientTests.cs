using System.Net;
using System.Net.Http.Json;
using Vaulta.App.Core.Orders;
using Vaulta.App.Core.Payments;
using Vaulta.App.Core.UnitTests.TestSupport;
using Vaulta.Payments.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Payments;

public sealed class PayoutsClientTests
{
    [Fact]
    public async Task UnregisteredKeyHasNoDestinationRatherThanJsonFailure()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            Assert.Equal("/api/v1/payouts/pix", request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.NoContent);
        });
        var client = new PayoutsClient(Http(handler));
        Assert.Null(await client.GetDestinationAsync());
    }

    [Fact]
    public async Task RegistersKeyWithoutClientVerificationFlag()
    {
        var handler = new FakeHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            var payload = await request.Content!.ReadAsStringAsync();
            Assert.DoesNotContain("verified", payload, StringComparison.OrdinalIgnoreCase);
            var sent = await request.Content.ReadFromJsonAsync<RegisterPixDestinationRequest>();
            Assert.Equal("seller@example.test", sent!.Key);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new PixDestinationDto("••••test", "EMAIL", "Seller", "PENDING_IDENTITY_REVIEW", null, Guid.NewGuid())) };
        });
        var result = await new PayoutsClient(Http(handler)).RegisterDestinationAsync(new("seller@example.test", "EMAIL"));
        Assert.Equal("PENDING_IDENTITY_REVIEW", result.Status);
    }

    [Fact]
    public async Task ReceiptUsesBuyersDeliverEndpointWithoutClientSuppliedTimestamp()
    {
        var id = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/orders/{id}/deliver", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content);
            return new(HttpStatusCode.NoContent);
        });
        await new OrdersClient(Http(handler)).ConfirmReceiptAsync(id);
    }

    private static HttpClient Http(FakeHttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://api.example.test/") };

    [Fact]
    public async Task CancelAcceptsRefundPending202AndReadsPrivateRefundDetails()
    {
        var id = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal($"/api/v1/orders/{id}/cancel", request.RequestUri!.AbsolutePath);
                var sent = await request.Content!.ReadFromJsonAsync<Vaulta.Orders.Contracts.CancelOrderRequest>();
                Assert.Equal("Antes do envio", sent!.Reason);
                return new(HttpStatusCode.Accepted);
            }
            Assert.Equal($"/api/v1/orders/{id}/refund", request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new OrderRefundDto(id, "PROCESSING", 100, DateTimeOffset.UtcNow, null)) };
        });
        var client = new OrdersClient(Http(handler));
        await client.CancelOrderAsync(id, "Antes do envio");
        Assert.Equal("PROCESSING", (await client.GetRefundAsync(id))!.Status);
    }
}
