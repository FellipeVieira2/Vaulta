using Vaulta.Payments.Application;
using Vaulta.Payments.Application.Commands;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Commerce.UnitTests;

public sealed class PaymentRetryTests
{
    [Fact]
    public async Task OverdueChargeCanStillBeReceivedAndReusesTheOriginalCharge()
    {
        var payments = new PaymentStore(); var gateway = new Gateway(); var orders = new OrderStore();
        var handler = Handler(payments, gateway, orders);
        var create = new InitiatePaymentCommand(orders.Order.BuyerId, new CreatePaymentRequest(orders.Order.Id, "PIX", "cus_test"));
        await handler.Handle(create, default);
        await handler.Handle(new ProcessWebhookCommand("PAYMENT_OVERDUE", "pay_test", "{}"), default);
        Assert.Equal(PaymentRules.OverdueStatus, payments.Transaction!.Status);
        await handler.Handle(create, default);
        Assert.Equal(1, gateway.Charges);
        await handler.Handle(new ProcessWebhookCommand("PAYMENT_RECEIVED", "pay_test", """{"payment":{"netValue":98}}"""), default);
        Assert.Equal(PaymentRules.ConfirmedStatus, payments.Transaction.Status);
        Assert.NotNull(payments.Transaction.ReceivedAt);
        Assert.Equal("paid", orders.Order.Status);
    }

    private static PaymentCommandHandlers Handler(PaymentStore payments, Gateway gateway, OrderStore orders) =>
        new(payments, gateway, orders, new TestClock(), new BuyerCustomers());

    [Fact]
    public async Task RetryingReturnsSameChargeWithoutCallingGatewayAgain()
    {
        var store = new PaymentStore();
        var gateway = new Gateway
        {
            BeforeCreate = () => Assert.Equal(1, store.Saves),
            InspectRequest = request => Assert.Empty(request.Splits)
        };
        var orders = new OrderStore();
        var handler = Handler(store, gateway, orders);
        var command = new InitiatePaymentCommand(orders.Order.BuyerId, new CreatePaymentRequest(orders.Order.Id, "PIX", "cus_test"));
        var first = await handler.Handle(command, default);
        var second = await handler.Handle(command, default);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("pay_test", second.AsaasPaymentId);
        Assert.Equal(1, gateway.Charges);
        Assert.Empty(store.Transaction!.Splits);
    }

    [Fact]
    public async Task LocalUniquenessFailurePreventsExternalCharge()
    {
        var store = new PaymentStore { SaveFailure = new ConflictException("Payment already reserved.") };
        var gateway = new Gateway();
        var orders = new OrderStore();
        await Assert.ThrowsAsync<ConflictException>(() => Handler(store, gateway, orders).Handle(
            new InitiatePaymentCommand(orders.Order.BuyerId, new CreatePaymentRequest(orders.Order.Id, "PIX", "cus_test")), default));
        Assert.Equal(0, gateway.Charges);
    }

    [Fact]
    public async Task MissingPaymentRejectsWebhookForRetryAndRecordsFailureOnce()
    {
        var store = new PaymentStore();
        await Assert.ThrowsAsync<ConflictException>(() => Handler(store, new Gateway(), new OrderStore()).Handle(
            new ProcessWebhookCommand("PAYMENT_RECEIVED", "pay_missing", "{}"), default));
        Assert.Single(store.Webhooks);
        Assert.False(store.Webhooks[0].Processed);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task VerifiedWebhookPersistsPaidOrderAndRepeatedEventDoesNotConfirmTwice()
    {
        var store = new PaymentStore();
        var orders = new OrderStore();
        var handler = Handler(store, new Gateway(), orders);
        await handler.Handle(new InitiatePaymentCommand(orders.Order.BuyerId,
            new CreatePaymentRequest(orders.Order.Id, "PIX", "cus_test")), default);
        var webhook = new ProcessWebhookCommand("PAYMENT_RECEIVED", "pay_test", """{"payment":{"netValue":98}}""");
        await handler.Handle(webhook, default);
        await handler.Handle(webhook, default);
        Assert.Equal("paid", orders.Order.Status);
        Assert.Equal(1, orders.Saves);
        Assert.Equal(PaymentRules.ConfirmedStatus, store.Transaction!.Status);
        Assert.Equal(98m, store.Transaction.NetAmount);
        Assert.Single(store.Webhooks);
    }
}
