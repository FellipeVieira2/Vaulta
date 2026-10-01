using Vaulta.Payments.Application;
using Vaulta.Payments.Contracts;
using Vaulta.Payments.Domain;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Commerce.UnitTests;

public sealed class SellerPayoutTests
{
    [Fact]
    public async Task BuyerPays100SellerPaysEightPercentAndActualProviderFeesAfterReceipt()
    {
        var f = new Fixture();
        Assert.Equal(100m, f.Orders.Order.TotalAmountBrl);
        Assert.Equal(8m, f.Store.Payout.PlatformFeeBrl);
        Assert.Equal(92m, f.Store.Payout.AmountBrl);
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(PayoutStatus.WaitingReceipt, f.Store.Payout.Status);
        f.Deliver();
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(PayoutStatus.WaitingSettlement, f.Store.Payout.Status);
        f.Payment.RecordSettlement(98m, f.Clock.UtcNow);
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(PayoutStatus.WaitingDestination, f.Store.Payout.Status);
        Assert.Equal(0, f.Gateway.Transfers);
        f.Verify();
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(PayoutStatus.Processing, f.Store.Payout.Status);
        Assert.Equal(90m, f.Gateway.LastRequest!.Amount);
        Assert.Equal(2m, f.Store.Payout.PaymentFeeBrl);
        Assert.Equal(1m, f.Store.Payout.TransferFeeBrl);
        Assert.Equal(89m, f.Store.Payout.SellerNetBrl);
        Assert.Equal("BRL", f.Store.Payout.Currency);
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(1, f.Gateway.Transfers);
    }

    [Fact]
    public async Task UnknownTransferResultIsReconciledWithoutResendingEvenWhenSearchIsEmpty()
    {
        var f = new Fixture(); f.Ready();
        f.Gateway.FailTransfer = true;
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(PayoutStatus.Reconciliation, f.Store.Payout.Status);
        await f.Service.Process(f.Store.Payout.Id, true, default);
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(1, f.Gateway.Transfers);
        Assert.Equal(2, f.Gateway.Searches);
        f.Gateway.Found = new("tr_recovered", "DONE", 90m, f.Store.Payout.ExternalReference, 89m, 1m);
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(PayoutStatus.Done, f.Store.Payout.Status);
        Assert.Equal(1, f.Gateway.Transfers);
    }

    [Fact]
    public async Task TransferWebhookMustMatchReferenceAmountAndExternalIdAndDoesNotRegress()
    {
        var f = new Fixture(); f.Ready();
        await f.Service.Process(f.Store.Payout.Id, true, default);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.ProcessWebhook("evt_wrong", "TRANSFER_DONE",
            new("tr_test", "DONE", 100m, f.Store.Payout.ExternalReference), default));
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.ProcessWebhook("evt_other_id", "TRANSFER_DONE",
            new("tr_other", "DONE", 92m, f.Store.Payout.ExternalReference), default));
        Assert.Empty(f.Store.Receipts);
        var completed = new PixTransfer("tr_test", "DONE", 90m, f.Store.Payout.ExternalReference, 89m, 1m);
        await f.Service.ProcessWebhook("evt_done", "TRANSFER_DONE", completed, default);
        await f.Service.ProcessWebhook("evt_done", "TRANSFER_DONE", completed, default);
        await f.Service.ProcessWebhook("evt_late_pending", "TRANSFER_PENDING", completed with { Status = "PENDING" }, default);
        Assert.Equal(PayoutStatus.Done, f.Store.Payout.Status);
        Assert.Equal(2, f.Store.Receipts.Count);
        Assert.Equal(1, f.Gateway.Transfers);
    }

    [Fact]
    public async Task ProviderFeesExceedingSellerProceedsCannotCreateATransfer()
    {
        var f = new Fixture(); f.Deliver(); f.Verify();
        f.Payment.RecordSettlement(7m, f.Clock.UtcNow);
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(PayoutStatus.InsufficientNet, f.Store.Payout.Status);
        Assert.Equal(0m, f.Store.Payout.AmountBrl);
        Assert.Equal(93m, f.Store.Payout.PaymentFeeBrl);
        Assert.Equal(0, f.Gateway.Transfers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HistoricalSplitOrUnavailableSellerBlocksDirectPayout(bool historicalSplit)
    {
        var f = new Fixture(); f.Ready();
        if (historicalSplit) f.Payment.AddSplit("historical_provider_wallet", null, 92m, null, null);
        else f.Identity.Available = false;
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(PayoutStatus.Blocked, f.Store.Payout.Status);
        Assert.Equal(0, f.Gateway.Transfers);
    }

    [Fact]
    public async Task DisabledTransfersKeepLedgerReadyWithoutMovingMoney()
    {
        var f = new Fixture(); f.Ready();
        await f.Service.Process(f.Store.Payout.Id, false, default);
        Assert.Equal(PayoutStatus.Ready, f.Store.Payout.Status);
        Assert.Null(f.Store.Payout.SubmittedAt);
        Assert.Equal(0, f.Gateway.Transfers);
    }

    [Fact]
    public async Task SellerCannotVerifyOwnKeyAndChangingKeyInvalidatesReview()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.VerifyDestination(f.Orders.Order.SellerId,
            f.Orders.Order.SellerId, new(f.Store.Destination!.Version, "identity-evidence-123"), default));
        f.Verify();
        var oldVersion = f.Store.Destination!.Version;
        f.Store.Destination.Replace("second@example.test", "EMAIL", "Seller", "***.202.745-**", f.Clock.UtcNow);
        Assert.False(f.Store.Destination.IsVerified);
        Assert.Throws<ConflictException>(() => f.Store.Destination.Verify(f.Identity.Reviewer, oldVersion, "proof-123", f.Clock.UtcNow));
        Assert.Throws<DomainException>(() => f.Store.Destination.Verify(f.Orders.Order.SellerId,
            f.Store.Destination.Version, "proof-123", f.Clock.UtcNow));
    }

    [Fact]
    public async Task RefundedPaymentNeverStartsTransfer()
    {
        var f = new Fixture(); f.Ready(); f.Payment.Refund(f.Clock.UtcNow);
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(PayoutStatus.Blocked, f.Store.Payout.Status);
        Assert.Equal(0, f.Gateway.Transfers);
    }

    [Fact]
    public async Task ChargebackHoldPreventsAnOtherwiseEligiblePayout()
    {
        var f = new Fixture(); f.Ready();
        f.Payment.HoldPayout("PAYMENT_CHARGEBACK_REQUESTED", f.Clock.UtcNow);
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(PayoutStatus.Blocked, f.Store.Payout.Status);
        Assert.Equal(0, f.Gateway.Transfers);
    }

    [Fact]
    public async Task CurrentProviderAccountMustConfirmSettlementBeforeAnyTransfer()
    {
        var f = new Fixture(); f.Ready(); f.Gateway.Settled = false;
        await f.Service.Process(f.Store.Payout.Id, true, default);
        Assert.Equal(PayoutStatus.Blocked, f.Store.Payout.Status);
        Assert.Null(f.Store.Payout.SubmittedAt);
        Assert.Equal(0, f.Gateway.Transfers);
    }

    private sealed class Fixture
    {
        public TestClock Clock { get; } = new();
        public OrderStore Orders { get; } = new();
        public PaymentStore Payments { get; } = new();
        public PaymentTransaction Payment => Payments.Transaction!;
        public FakePayoutStore Store { get; }
        public FakePixGateway Gateway { get; } = new();
        public FakeIdentity Identity { get; } = new();
        public SellerPayoutService Service { get; }
        public Fixture()
        {
            Payments.Add(PaymentTransaction.Create(Orders.Order.Id, Orders.Order.BuyerId, Orders.Order.SellerId, 100m, "PIX", "cus_test", Clock.UtcNow));
            Payment.SetCheckoutInfo("pay_test", null, null, null);
            Payment.Confirm("pay_test", 98m, Clock.UtcNow);
            Orders.Order.MarkAsPaid("pay_test", Clock.UtcNow);
            Store = new FakePayoutStore
            {
                Payout = SellerPayout.Create(Orders.Order.Id, Payment.Id, Orders.Order.SellerId, 100m, 8m, Clock.UtcNow),
                Destination = SellerPixDestination.Register(Orders.Order.SellerId, "seller@example.test", "EMAIL", "Seller", "***.202.745-**", Clock.UtcNow)
            };
            Service = new(Store, Payments, Orders, Gateway, Identity, Clock);
        }
        public void Deliver() { Orders.Order.MarkAsShipped("tracking", Clock.UtcNow); Orders.Order.MarkAsDelivered(Clock.UtcNow); }
        public void Verify() => Store.Destination!.Verify(Identity.Reviewer, Store.Destination.Version, "identity-evidence-123", Clock.UtcNow);
        public void Ready() { Deliver(); Verify(); Payment.RecordSettlement(98m, Clock.UtcNow); }
    }

    private sealed class FakeIdentity : IPayoutIdentity
    {
        public Guid Reviewer { get; } = Guid.NewGuid();
        public bool Available { get; set; } = true;
        public Task<bool> IsAvailable(Guid id, CancellationToken ct) => Task.FromResult(Available);
        public bool CanReview(Guid id) => id == Reviewer;
    }
    private sealed class FakePixGateway : IPixPayoutGateway
    {
        public int Transfers { get; private set; }
        public int Searches { get; private set; }
        public bool FailTransfer { get; set; }
        public bool Settled { get; set; } = true;
        public Task<bool> IsPaymentSettled(string id, Guid orderId, decimal amount, decimal minimumNet, CancellationToken ct) => Task.FromResult(Settled);
        public PixTransferRequest? LastRequest { get; private set; }
        public PixTransfer? Found { get; set; }
        public Task<PixHolder> Lookup(string key, string type, CancellationToken ct) => Task.FromResult(new PixHolder(key, type, "Seller", "***.202.745-**"));
        public Task<PixTransfer> Transfer(PixTransferRequest request, CancellationToken ct)
        {
            Transfers++; LastRequest = request;
            return FailTransfer ? Task.FromException<PixTransfer>(new HttpRequestException("unknown response"))
                : Task.FromResult(new PixTransfer("tr_test", "PENDING", request.Amount, request.ExternalReference, request.Amount - 1, 1));
        }
        public Task<PixTransfer?> FindTransfer(string? id, string reference, DateTimeOffset submittedAt, CancellationToken ct)
        { Searches++; return Task.FromResult(Found); }
    }
    private sealed class FakePayoutStore : IPayoutStore
    {
        public SellerPayout Payout { get; set; } = null!;
        public SellerPixDestination? Destination { get; set; }
        public List<TransferWebhookReceipt> Receipts { get; } = [];
        public Task<SellerPixDestination?> FindDestination(Guid id, CancellationToken ct) => Task.FromResult(Destination);
        public void Add(SellerPixDestination destination) => Destination = destination;
        public void Add(SellerPayout payout) => Payout = payout;
        public void Add(TransferWebhookReceipt receipt) => Receipts.Add(receipt);
        public Task<SellerPayout?> Find(Guid id, CancellationToken ct) => Task.FromResult<SellerPayout?>(Payout.Id == id ? Payout : null);
        public Task<SellerPayout?> FindByReference(string reference, CancellationToken ct) => Task.FromResult<SellerPayout?>(Payout.ExternalReference == reference ? Payout : null);
        public Task<bool> HasWebhook(string id, CancellationToken ct) => Task.FromResult(Receipts.Any(x => x.Id == id));
        public Task<IReadOnlyList<Guid>> UnrecordedPayments(CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<IReadOnlyList<Guid>> Due(DateTimeOffset now, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([Payout.Id]);
        public Task<bool> Claim(SellerPayout payout, DateTimeOffset now, CancellationToken ct) { payout.MarkClaimed(now, Guid.NewGuid()); return Task.FromResult(true); }
        public Task<SellerPayoutPageDto> ListMine(Guid seller, int page, int size, CancellationToken ct) => throw new NotSupportedException();
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }
}
