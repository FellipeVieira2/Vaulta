using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vaulta.Payments.Application;
using Vaulta.Payments.Infrastructure;
using Xunit;

namespace Vaulta.Commerce.UnitTests;

public sealed class AsaasPixPayoutGatewayTests
{
    [Theory]
    [InlineData("RECEIVED", 100, 98, true)]
    [InlineData("CONFIRMED", 100, 98, false)]
    [InlineData("REFUNDED", 100, 98, false)]
    [InlineData("RECEIVED", 108, 98, false)]
    [InlineData("RECEIVED", 100, 91, false)]
    [InlineData("RECEIVED", 100, 99, false)]
    public async Task ChecksActualProviderChargeBeforeReleasingPayout(string status, int amount, int net, bool expected)
    {
        var orderId = Guid.NewGuid();
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v3/payments/pay_test", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { id = "pay_test", status, value = amount, netValue = net, externalReference = orderId.ToString(), deleted = false })
            });
        });
        Assert.Equal(expected, await Gateway(handler).IsPaymentSettled("pay_test", orderId, 100, 98, default));
    }

    [Fact]
    public async Task TransferSendsSellerNetAmountAndPixDestinationWithStableReference()
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v3/transfers", request.RequestUri!.AbsolutePath);
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = payload.RootElement;
            Assert.Equal(92m, root.GetProperty("value").GetDecimal());
            Assert.Equal("PIX", root.GetProperty("operationType").GetString());
            Assert.Equal("seller@example.test", root.GetProperty("pixAddressKey").GetString());
            Assert.Equal("EMAIL", root.GetProperty("pixAddressKeyType").GetString());
            Assert.Equal("vaulta_payout_test", root.GetProperty("externalReference").GetString());
            Assert.False(root.TryGetProperty("walletId", out _));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "tr_test", status = "PENDING", value = 92m, netValue = 91m, transferFee = 1m, externalReference = "vaulta_payout_test" }) };
        });
        var result = await Gateway(handler).Transfer(new(92m, "seller@example.test", "EMAIL", "vaulta_payout_test"), default);
        Assert.Equal("tr_test", result.Id); Assert.Equal("PENDING", result.Status);
        Assert.Equal(91m, result.NetValue); Assert.Equal(1m, result.TransferFee);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ServerErrorDoesNotRetryTransferPostOrExposeResponseBody()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        { Content = new StringContent("private-provider-detail") }));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Gateway(handler).Transfer(new(92, "seller@example.test", "EMAIL", "reference"), default));
        Assert.DoesNotContain("private-provider-detail", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ReconciliationPaginatesAndMatchesReferenceWithoutCreatingTransfer()
    {
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var offset = request.RequestUri!.Query.Contains("offset=100", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    hasMore = !offset,
                    data = new[] { new { id = offset ? "tr_matching" : "tr_unrelated", status = "DONE", value = 92m, netValue = 91m, transferFee = 1m, externalReference = offset ? "desired" : "other" } }
                })
            });
        });
        var result = await Gateway(handler).FindTransfer(null, "desired", DateTimeOffset.UtcNow, default);
        Assert.Equal("tr_matching", result!.Id); Assert.Equal(2, handler.Calls);
    }

    private static AsaasPixPayoutGateway Gateway(Handler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://provider.example.test/") };
        http.DefaultRequestHeaders.Add("access_token", "fake-test-token");
        return new(http);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return callback(request); }
    }
}
