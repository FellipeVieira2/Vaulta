using Vaulta.Orders.Application;
using Vaulta.Orders.Application.Commands;
using Vaulta.Orders.Domain;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Commerce.UnitTests;

public sealed class OrderPaymentSecurityTests
{
    [Fact]
    public void PaidOrderCannotBeCancelledWithoutRefund()
    {
        var store = new OrderStore();
        store.Order.MarkAsPaid("pay_test", new TestClock().UtcNow);
        Assert.Throws<DomainException>(() => store.Order.Cancel("requested", new TestClock().UtcNow));
        Assert.Equal(OrderRules.PaidStatus, store.Order.Status);
        Assert.Null(store.Order.CancelledAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NeitherBuyerNorAnotherUserCanDeclarePayment(bool buyer)
    {
        var store = new OrderStore();
        var handler = new OrderCommandHandlers(store, new Marketplace(), new TestClock(), new OrderCollection());
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new ConfirmPaymentCommand(buyer ? store.Order.BuyerId : Guid.NewGuid(), store.Order.Id, "pay_forged"), default));
        Assert.Equal(OrderRules.PendingStatus, store.Order.Status);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task SellerCannotConfirmReceiptButBuyerCan()
    {
        var store = new OrderStore();
        var clock = new TestClock();
        store.Order.MarkAsPaid("pay_verified", clock.UtcNow);
        store.Order.MarkAsShipped("tracking", clock.UtcNow);
        var collection = new OrderCollection();
        var handler = new OrderCommandHandlers(store, new Marketplace(), clock, collection);
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new MarkDeliveredCommand(store.Order.SellerId, store.Order.Id), default));
        Assert.Equal(OrderRules.ShippedStatus, store.Order.Status);
        await handler.Handle(new MarkDeliveredCommand(store.Order.BuyerId, store.Order.Id), default);
        await handler.Handle(new MarkDeliveredCommand(store.Order.BuyerId, store.Order.Id), default);
        Assert.Equal(OrderRules.DeliveredStatus, store.Order.Status);
        Assert.Equal(1, store.Saves);
        Assert.Equal(2, collection.Transfers);
    }
}
