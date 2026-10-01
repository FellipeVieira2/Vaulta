using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vaulta.Orders.Domain;
using Vaulta.Payments.Application;
using Vaulta.Payments.Application.Commands;
using Vaulta.Payments.Domain;
using Vaulta.Payments.Infrastructure;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Commerce.UnitTests;

public sealed class PaymentRefundTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EitherPartyCanRequestBeforeShippingButOnlyConfirmedFullReturnFinishes(bool buyer)
    {
        var f = new Fixture();
        var user = buyer ? f.Orders.Order.BuyerId : f.Orders.Order.SellerId;
        await f.Service.Request(user, f.Orders.Order.Id, "Cancelamento antes do envio", default);
        await f.Service.Request(user, f.Orders.Order.Id, "Repetição", default);
        Assert.Equal(OrderRules.RefundPendingStatus, f.Orders.Order.Status);
        Assert.Equal("REFUND_REQUESTED", f.Payment.PayoutHoldReason);
        Assert.Throws<DomainException>(() => f.Orders.Order.MarkAsShipped("tracking", f.Clock.UtcNow));
        await f.Service.Process(f.Payment.Id, true, default);
        Assert.Equal(1, f.Gateway.Requests);
        Assert.Equal(100m, f.Gateway.Amount);
        Assert.Equal(OrderRules.RefundPendingStatus, f.Orders.Order.Status);
        f.Gateway.State = new(false, 100, 0);
        await f.Service.Process(f.Payment.Id, true, default);
        await f.Service.Process(f.Payment.Id, true, default);
        Assert.Equal(1, f.Gateway.Requests);
        Assert.Equal(OrderRules.RefundedStatus, f.Orders.Order.Status);
        Assert.Equal(PaymentRules.RefundedStatus, f.Payment.Status);
    }

    [Fact]
    public async Task OtherUserAndOrdersAlreadyShippedRequireSupportWithoutCallingProvider()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.Request(Guid.NewGuid(), f.Orders.Order.Id, "Outro", default));
        f.Orders.Order.MarkAsShipped("tracking", f.Clock.UtcNow);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.Request(f.Orders.Order.BuyerId, f.Orders.Order.Id, "Cancelar", default));
        Assert.Null(f.Payment.RefundRequestedAt);
        Assert.Equal(0, f.Gateway.Requests);
    }

    [Fact]
    public async Task TimeoutAfterPostNeverResendsEvenIfProviderGetShowsNoRefundYet()
    {
        var f = new Fixture(); f.Gateway.FailRequest = true;
        await f.Service.Request(f.Orders.Order.BuyerId, f.Orders.Order.Id, "Cancelar", default);
        await f.Service.Process(f.Payment.Id, true, default);
        Assert.Equal("RECONCILIATION_REQUIRED", f.Payment.RefundStatus);
        await f.Service.Process(f.Payment.Id, true, default);
        await f.Service.Process(f.Payment.Id, true, default);
        Assert.Equal(1, f.Gateway.Requests);
        Assert.Equal(OrderRules.RefundPendingStatus, f.Orders.Order.Status);
    }

    [Fact]
    public async Task ProviderGetFailureBeforeClaimCanRecoverWithoutLosingRequest()
    {
        var f = new Fixture(); f.Gateway.FailGet = true;
        await f.Service.Request(f.Orders.Order.BuyerId, f.Orders.Order.Id, "Cancelar", default);
        await f.Service.Process(f.Payment.Id, true, default);
        Assert.Equal("REQUESTED", f.Payment.RefundStatus);
        Assert.Null(f.Payment.RefundSubmittedAt);
        f.Gateway.FailGet = false;
        await f.Service.Process(f.Payment.Id, true, default);
        Assert.Equal(1, f.Gateway.Requests);
    }

    [Fact]
    public async Task DisabledRequestsHoldPaidOrderWithoutCallingProvider()
    {
        var f = new Fixture();
        await f.Service.Request(f.Orders.Order.BuyerId, f.Orders.Order.Id, "Cancelar", default);
        await f.Service.Process(f.Payment.Id, false, default);
        Assert.Equal(0, f.Gateway.Requests);
        Assert.Equal(0, f.Gateway.Reads);
        Assert.Equal(OrderRules.RefundPendingStatus, f.Orders.Order.Status);
    }

    [Fact]
    public async Task ConfirmedPixWaitsForSettlementThenRequestsRefundOnce()
    {
        var f = new Fixture(); f.Gateway.State = new(false, 0, 0, AwaitingSettlement: true);
        await f.Service.Request(f.Orders.Order.BuyerId, f.Orders.Order.Id, "Cancelar", default);
        await f.Service.Process(f.Payment.Id, true, default);
        Assert.Equal("REQUESTED", f.Payment.RefundStatus); Assert.Equal(0, f.Gateway.Requests);
        f.Gateway.State = new(true, 0, 0);
        await f.Service.Process(f.Payment.Id, true, default);
        Assert.Equal(1, f.Gateway.Requests);
    }

    [Fact]
    public async Task PaymentArrivingAfterUnpaidCancellationIsQueuedForRefundWithoutReactivatingTheOrder()
    {
        var orders = new OrderStore(); var payments = new PaymentStore(); var clock = new TestClock();
        var handler = new PaymentCommandHandlers(payments, new Gateway(), orders, clock, new BuyerCustomers());
        await handler.Handle(new InitiatePaymentCommand(orders.Order.BuyerId, new(orders.Order.Id, "PIX", "cus_test")), default);
        orders.Order.Cancel("Antes da confirmação", clock.UtcNow);
        await handler.Handle(new ProcessWebhookCommand("PAYMENT_RECEIVED", "pay_test", """{"payment":{"netValue":98}}"""), default);
        Assert.Equal(OrderRules.CancelledStatus, orders.Order.Status);
        Assert.NotNull(orders.Order.PaidAt);
        Assert.Equal("REQUESTED", payments.Transaction!.RefundStatus);
        Assert.Null(payments.Transaction.RefundRequestedBy);
        var gateway = new RefundGateway();
        var service = new PaymentRefundService(payments, orders, new RefundStoreFake(), gateway, clock);
        await service.Process(payments.Transaction.Id, true, default);
        Assert.Equal(1, gateway.Requests);
        gateway.State = new(false, 100, 0);
        await service.Process(payments.Transaction.Id, true, default);
        Assert.Equal(OrderRules.RefundedStatus, orders.Order.Status);
        await handler.Handle(new ProcessWebhookCommand("PAYMENT_CONFIRMED", "pay_test", "{}"), default);
        Assert.Equal(PaymentRules.RefundedStatus, payments.Transaction.Status);
    }

    [Fact]
    public async Task PartialRefundCannotBeMarkedAsFullOrCauseASecondRequest()
    {
        var f = new Fixture(); f.Gateway.State = new(true, 20, 0);
        await f.Service.Request(f.Orders.Order.BuyerId, f.Orders.Order.Id, "Cancelar", default);
        await f.Service.Process(f.Payment.Id, true, default);
        Assert.Equal("RECONCILIATION_REQUIRED", f.Payment.RefundStatus);
        Assert.Equal(0, f.Gateway.Requests);
        Assert.Equal(OrderRules.RefundPendingStatus, f.Orders.Order.Status);
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(100, true)]
    public async Task WebhookRequiresCompletedFullRefundBeforeChangingOrder(decimal amount, bool valid)
    {
        var f = new Fixture();
        await f.Service.Request(f.Orders.Order.SellerId, f.Orders.Order.Id, "Cancelar", default);
        var payload = JsonSerializer.Serialize(new { payment = new { id = "pay_test", status = "REFUNDED", value = 100,
            refunds = new[] { new { status = "DONE", value = amount } } } });
        var handler = new PaymentCommandHandlers(f.Payments, new Gateway(), f.Orders, f.Clock, new BuyerCustomers());
        var action = () => handler.Handle(new ProcessWebhookCommand("PAYMENT_REFUNDED", "pay_test", payload), default);
        if (!valid) await Assert.ThrowsAsync<ConflictException>(action);
        else { await action(); await action(); }
        Assert.Equal(valid ? OrderRules.RefundedStatus : OrderRules.RefundPendingStatus, f.Orders.Order.Status);
    }

    [Theory]
    [InlineData("PENDING", "RECEIVED", 0, 100)]
    [InlineData("DONE", "REFUNDED", 100, 0)]
    [InlineData("CANCELLED", "RECEIVED", 0, 0)]
    public async Task GatewayReadsEveryRefundStatusAndSendsGrossAmount(string status, string paymentStatus, decimal done, decimal pending)
    {
        var orderId = Guid.NewGuid();
        var handler = new HttpHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v3/payments/pay_test/refund", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(100, body.RootElement.GetProperty("value").GetDecimal());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "pay_test", value = 100,
                externalReference = orderId.ToString(), status = paymentStatus, refunds = new[] { new { status, value = 100 } } }) };
        });
        var http = new HttpClient(handler) { BaseAddress = new("https://provider.example.test") };
        http.DefaultRequestHeaders.Add("access_token", "test");
        var state = await new AsaasRefundGateway(http).Request("pay_test", orderId, 100, "PIX", "Cancelar", default);
        Assert.Equal(done, state.CompletedAmount); Assert.Equal(pending, state.PendingAmount);
    }

    private sealed class Fixture
    {
        public TestClock Clock { get; } = new();
        public OrderStore Orders { get; } = new();
        public PaymentStore Payments { get; } = new();
        public PaymentTransaction Payment => Payments.Transaction!;
        public RefundGateway Gateway { get; } = new();
        public PaymentRefundService Service { get; }
        public Fixture()
        {
            var o = Orders.Order;
            Payments.Add(PaymentTransaction.Create(o.Id, o.BuyerId, o.SellerId, 100, "PIX", null, Clock.UtcNow));
            Payment.SetCheckoutInfo("pay_test", null, null, null);
            Payment.Confirm("pay_test", 98, Clock.UtcNow);
            Payment.RecordSettlement(98, Clock.UtcNow);
            o.MarkAsPaid("pay_test", Clock.UtcNow);
            Service = new(Payments, Orders, new RefundStoreFake(), Gateway, Clock);
        }
    }
    private sealed class RefundStoreFake : IRefundStore
    {
        public Task<IReadOnlyList<Guid>> Due(DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> Claim(PaymentTransaction payment, DateTimeOffset now, CancellationToken ct)
        { payment.ClaimRefund(now, Guid.NewGuid()); return Task.FromResult(true); }
    }
    private sealed class RefundGateway : IRefundGateway
    {
        public int Requests { get; private set; }
        public int Reads { get; private set; }
        public decimal Amount { get; private set; }
        public bool FailRequest { get; set; }
        public bool FailGet { get; set; }
        public ProviderRefund State { get; set; } = new(true, 0, 0);
        public Task<ProviderRefund> Get(string id, Guid order, decimal amount, CancellationToken ct)
        { Reads++; return FailGet ? Task.FromException<ProviderRefund>(new HttpRequestException()) : Task.FromResult(State); }
        public Task<ProviderRefund> Request(string id, Guid order, decimal amount, string type, string reason, CancellationToken ct)
        { Requests++; Amount = amount; return FailRequest ? Task.FromException<ProviderRefund>(new HttpRequestException()) : Task.FromResult(new ProviderRefund(false, 0, amount)); }
    }
    private sealed class HttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request);
    }
}
