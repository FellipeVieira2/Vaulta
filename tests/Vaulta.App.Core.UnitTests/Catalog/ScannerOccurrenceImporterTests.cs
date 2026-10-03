using System.Net;
using System.Net.Http.Json;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Collection;
using Vaulta.Collection.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class ScannerOccurrenceImporterTests
{
    [Fact]
    public async Task PendingOccurrenceAndAllPendingSessionAreBlockedBeforeWritesOrFreeze()
    {
        var initial = Session();
        var visual = new Vaulta.Catalog.Contracts.CardVisualIdentificationDto("Card", null, "pt", null, .8);
        var session = initial with { Cards = initial.Cards.Select(x => x with { PrintingId = Guid.Empty, VisualIdentification = visual }).ToArray() };
        var store = new MemoryStore(session); var handler = new Receipts();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(store, handler, () => session.OwnerId).Import(session, session.Cards[0].ScanId));
        Assert.Same(session, store.Value);
        var completed = session.Complete(); await store.Save(completed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ScannerSessionImporter(Client(handler), store, () => session.OwnerId).Import(completed));
        Assert.Same(completed, store.Value);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MixedSessionImportsResolvedCopiesIdempotentlyAndLeavesPendingVisible()
    {
        var initial = Session();
        var pending = initial.Cards[0] with { ScanId = Guid.NewGuid(), PrintingId = Guid.Empty,
            VisualIdentification = new("Card", null, "pt", null, .8) };
        var session = initial.Add(pending).Complete();
        var store = new MemoryStore(session); var handler = new Receipts();
        var importer = new ScannerSessionImporter(Client(handler), store, () => session.OwnerId);
        var imported = await importer.Import(session);
        Assert.Equal(ScannerSessionPhase.Imported, imported.Phase);
        Assert.Equal(3, imported.Cards.Count);
        Assert.Null(imported.Cards.Single(x => x.ScanId == pending.ScanId).ImportedItemId);
        await importer.Import(imported);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, x => Assert.NotEqual(Guid.Empty, x.PrintingId));
    }

    [Fact]
    public async Task CertificateFromThePhotoIsRetainedInCollectionNotesOnBothImportPaths()
    {
        var initial = Session();
        var visual = new Vaulta.Catalog.Contracts.CardVisualIdentificationDto("Card", "1", "en", null, .99,
            Certification: new("PSA", "10", "01234567"));
        var session = initial with { Cards = initial.Cards.Select(x => x with { VisualIdentification = visual, MarketValue = null }).ToArray() };
        var store = new MemoryStore(session); var handler = new Receipts();
        var one = await Service(store, handler, () => session.OwnerId).Import(session, session.Cards[0].ScanId);
        var completed = one.Complete(); await store.Save(completed);
        await new ScannerSessionImporter(Client(handler), store, () => session.OwnerId).Import(completed);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Contains("01234567", request.Notes);
            Assert.Contains("não verificada", request.Notes);
            Assert.Null(request.AcquisitionPrice);
        });
    }

    [Fact]
    public async Task ImportsOnlyChosenOccurrenceAndRetainsScanningPhase()
    {
        var session = Session(); var store = new MemoryStore(session); var handler = new Receipts();
        var service = Service(store, handler, () => session.OwnerId);
        var result = await service.Import(session, session.Cards[0].ScanId);
        Assert.Equal(ScannerSessionPhase.Scanning, result.Phase);
        Assert.NotNull(result.Cards[0].ImportedItemId); Assert.Null(result.Cards[1].ImportedItemId);
        Assert.Single(handler.Keys); Assert.Null(handler.Requests[0].AcquisitionPrice);
        Assert.Equal("UNKNOWN", handler.Requests[0].Condition);
    }

    [Fact]
    public async Task ResponseLostRetryUsesFrozenPayloadAndReturnsSameUnit()
    {
        var session = Session(); var store = new MemoryStore(session); var handler = new Receipts { LoseFirstResponse = true };
        var service = Service(store, handler, () => session.OwnerId); var scan = session.Cards[0].ScanId;
        await Assert.ThrowsAsync<HttpRequestException>(() => service.Import(session, scan));
        Assert.True(store.Value.Cards[0].ImportStarted);
        var imported = await service.Import(store.Value, scan);
        Assert.Equal(handler.Keys.Single().Value, imported.Cards[0].ImportedItemId);
        await service.Import(imported, scan); Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Payloads[0], handler.Payloads[1]);
    }

    [Fact]
    public async Task DistinctScanIdsOfSamePrintingCreateTwoUnits()
    {
        var session = Session(); var store = new MemoryStore(session); var handler = new Receipts();
        var service = Service(store, handler, () => session.OwnerId);
        var first = await service.Import(session, session.Cards[0].ScanId);
        var second = await service.Import(first, session.Cards[1].ScanId);
        Assert.NotEqual(second.Cards[0].ImportedItemId, second.Cards[1].ImportedItemId); Assert.Equal(2, handler.Keys.Count);
    }

    [Fact]
    public async Task AccountChangeAfterResponseDoesNotPersistPrivateIdsToNewAccount()
    {
        var session = Session(); var owner = session.OwnerId; var store = new MemoryStore(session);
        var handler = new Receipts { AfterRequest = () => owner = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(store, handler, () => owner).Import(session, session.Cards[0].ScanId));
        Assert.Null(store.Value.Cards[0].ImportedItemId); Assert.Equal(session.OwnerId, store.Value.OwnerId);
    }

    [Fact]
    public async Task FrozenOccurrenceCannotBeRemovedAndSessionBulkImportDoesNotDuplicate()
    {
        var session = Session(); var store = new MemoryStore(session); var handler = new Receipts();
        var first = await Service(store, handler, () => session.OwnerId).Import(session, session.Cards[0].ScanId);
        Assert.Throws<InvalidOperationException>(() => first.Remove(first.Cards[0].ScanId));
        var completed = first.Complete(); await store.Save(completed);
        var all = await new ScannerSessionImporter(Client(handler), store, () => session.OwnerId).Import(completed);
        Assert.Equal(ScannerSessionPhase.Imported, all.Phase); Assert.Equal(2, handler.Keys.Count);
    }

    [Fact]
    public async Task ConcurrentCallsForSameOccurrenceSendOnlyOneRequest()
    {
        var session = Session(); var store = new MemoryStore(session); var handler = new Receipts();
        var service = Service(store, handler, () => session.OwnerId);
        var results = await Task.WhenAll(service.Import(session, session.Cards[0].ScanId), service.Import(session, session.Cards[0].ScanId));
        Assert.Equal(results[0].Cards[0].ImportedItemId, results[1].Cards[0].ImportedItemId); Assert.Single(handler.Requests);
    }

    private static ScannerSession Session()
    {
        var now = DateTimeOffset.UtcNow; var printing = Guid.NewGuid(); var session = ScannerSession.Start(Guid.NewGuid(), now);
        for (var i = 0; i < 2; i++) session = session.Add(new(Guid.NewGuid(), printing, null, "Card", "Set", "1", "Normal", "UNKNOWN", null, new(100, "reference", now), now));
        return session;
    }
    private static CollectionClient Client(Receipts handler) => new(new HttpClient(handler) { BaseAddress = new("https://example.test/") });
    private static ScannerOccurrenceImporter Service(MemoryStore store, Receipts handler, Func<Guid?> owner) => new(Client(handler), store, owner);
    private sealed class MemoryStore(ScannerSession session) : IScannerSessionStore
    {
        public ScannerSession Value { get; private set; } = session;
        public Task<ScannerSession?> Latest(Guid owner, CancellationToken ct = default) => Task.FromResult<ScannerSession?>(Value.OwnerId == owner ? Value : null);
        public Task Save(ScannerSession value, CancellationToken ct = default) { Value = value; return Task.CompletedTask; }
    }
    private sealed class Receipts : HttpMessageHandler
    {
        public bool LoseFirstResponse { get; init; }
        public Action? AfterRequest { get; init; }
        public Dictionary<string, Guid> Keys { get; } = [];
        public List<AddCollectibleItemsRequest> Requests { get; } = [];
        public List<string> Payloads { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = Assert.Single(request.Headers.GetValues("Idempotency-Key"));
            Payloads.Add(await request.Content!.ReadAsStringAsync(ct));
            Requests.Add((await request.Content.ReadFromJsonAsync<AddCollectibleItemsRequest>(ct))!);
            if (!Keys.TryGetValue(key, out var item)) { item = Guid.NewGuid(); Keys[key] = item; }
            AfterRequest?.Invoke();
            if (LoseFirstResponse && Requests.Count == 1) throw new HttpRequestException("Response lost");
            return new(HttpStatusCode.Created) { Content = JsonContent.Create(new AddCollectibleItemsResponse(Guid.NewGuid(), [new(item, Guid.NewGuid())], 1)) };
        }
    }
}
