using Vaulta.Marketplace.Contracts;
using Vaulta.Orders.Application;
using Vaulta.Orders.Domain;
using Vaulta.Payments.Application;
using Vaulta.Payments.Domain;
using Vaulta.SharedKernel;
using Vaulta.Wallets.Application;
using Vaulta.Wallets.Domain;

namespace Vaulta.Commerce.UnitTests;

internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
}

internal sealed class OrderStore : IOrderStore
{
    public Order Order { get; } = Order.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), null, "NM", 100m, 8m, null, null, null, null, null, null, null, null, new TestClock().UtcNow);
    public int Saves { get; private set; }
    public Task<Order?> FindOrder(Guid id, CancellationToken ct) => Task.FromResult<Order?>(id == Order.Id ? Order : null);
    public Task<Order?> FindOrderForUser(Guid user, Guid id, CancellationToken ct) => FindOrder(id, ct);
    public Task<Reservation?> FindActiveReservation(Guid id, CancellationToken ct) => Task.FromResult<Reservation?>(null);
    public Task<Reservation?> FindReservationByBuyerAndListing(Guid buyer, Guid id, CancellationToken ct) => Task.FromResult<Reservation?>(null);
    public void AddReservation(Reservation reservation) => throw new NotSupportedException();
    public void AddOrder(Order order) => throw new NotSupportedException();
    public Task<IReadOnlyList<Order>> ReleasableOrders(int offset, CancellationToken ct) => Task.FromResult<IReadOnlyList<Order>>([]);
    public Task Save(CancellationToken ct) { Saves++; return Task.CompletedTask; }
}

internal sealed class Marketplace : IOrderMarketplace
{
    public Task<ListingDto?> GetActiveListing(Guid id, CancellationToken ct) => Task.FromResult<ListingDto?>(null);
    public Task MarkListingAsSold(Guid id, Guid order, DateTimeOffset now, CancellationToken ct) => Task.CompletedTask;
    public Task ReleaseListing(Order order, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class OrderCollection : IOrderCollection
{
    public int Transfers { get; private set; }
    public Task TransferDeliveredItem(Order order, CancellationToken ct) { Transfers++; return Task.CompletedTask; }
}

internal sealed class PaymentStore : IPaymentStore
{
    public PaymentTransaction? Transaction { get; private set; }
    public List<WebhookEvent> Webhooks { get; } = [];
    public int Saves { get; private set; }
    public Exception? SaveFailure { get; set; }
    public void Add(PaymentTransaction transaction) => Transaction = transaction;
    public void AddWebhookEvent(WebhookEvent value) => Webhooks.Add(value);
    public Task<PaymentTransaction?> FindById(Guid id, CancellationToken ct) => Task.FromResult(Transaction);
    public Task<PaymentTransaction?> FindByOrderId(Guid id, CancellationToken ct) => Task.FromResult(Transaction);
    public Task<PaymentTransaction?> FindByAsaasId(string id, CancellationToken ct) => Task.FromResult(Transaction?.AsaasPaymentId == id ? Transaction : null);
    public Task<bool> HasProcessedWebhook(string id, string type, CancellationToken ct) =>
        Task.FromResult(Webhooks.Any(x => x.AsaasPaymentId == id && x.EventType == type && x.Processed));
    public Task Save(CancellationToken ct)
    {
        Saves++;
        return SaveFailure is null ? Task.CompletedTask : Task.FromException(SaveFailure);
    }
}

internal sealed class Gateway : IPaymentGateway
{
    public int Charges { get; private set; }
    public Action? BeforeCreate { get; set; }
    public Action<GatewayPaymentRequest>? InspectRequest { get; set; }
    public CreatePaymentResult? Recovered { get; set; }
    public Task<CreatePaymentResult?> FindPaymentAsync(GatewayPaymentRequest request, CancellationToken ct) => Task.FromResult(Recovered);
    public bool FailPix { get; set; }
    public Action? BeforePixRead { get; set; }
    public string PixPayload { get; set; } = "pix-copy-paste-test";
    public string? PixExpiration { get; set; } = "2026-10-02 23:59:59";
    public Task<GatewayPixPayload> GetPixPayloadAsync(string paymentId, CancellationToken ct)
    {
        BeforePixRead?.Invoke();
        return FailPix ? Task.FromException<GatewayPixPayload>(new HttpRequestException("Pix QR unavailable")) : Task.FromResult(new GatewayPixPayload(PixPayload, PixExpiration));
    }
    public Task<CreatePaymentResult> CreatePaymentAsync(GatewayPaymentRequest request, CancellationToken ct)
    {
        BeforeCreate?.Invoke();
        InspectRequest?.Invoke(request);
        Charges++;
        return Task.FromResult(new CreatePaymentResult("pay_test", "https://sandbox.test/checkout", null, null, "PENDING"));
    }
}

internal sealed class BuyerCustomers : IBuyerCustomerAccess
{
    public Task<string> Resolve(Guid userId, CancellationToken ct) => Task.FromResult("cus_test");
}

internal sealed class WalletStore : IWalletStore
{
    public Task<Wallet?> FindByUserId(Guid id, CancellationToken ct) => Task.FromResult<Wallet?>(null);
    public Task<Wallet?> GetById(Guid id, CancellationToken ct) => Task.FromResult<Wallet?>(null);
    public Task<bool> HasLedgerEntry(Guid id, string reference, string type, CancellationToken ct) => Task.FromResult(false);
    public void Add(Wallet wallet) => throw new NotSupportedException();
    public Task Save(CancellationToken ct) => Task.CompletedTask;
}
