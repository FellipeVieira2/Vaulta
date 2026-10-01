using System.Net;
using System.Net.Http.Json;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Collection;
using Vaulta.Collection.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class ScannerSessionRecoveryTests
{
    [Fact]
    public async Task LostImportResponseCanResumeFromDiskWithoutCountingASecondPhysicalCopy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vaulta-session-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileScannerSessionStore(directory); var owner = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var card = new ScannerSessionCard(Guid.NewGuid(), Guid.NewGuid(), null, "Card", "Set", "1", "Normal", "UNKNOWN", null, new(150, "TCGplayer", now), now);
            var session = ScannerSession.Start(owner, now).ConfigureCost(5, PackCostMode.PerPack, 20).Add(card).Complete();
            var handler = new LostResponse(); var client = new CollectionClient(new HttpClient(handler) { BaseAddress = new("https://example.test") });
            var importer = new ScannerSessionImporter(client, store, () => owner);
            await Assert.ThrowsAsync<HttpRequestException>(() => importer.Import(session));
            Assert.Equal(1, handler.CreatedCopies);
            var restored = (await store.Latest(owner))!; Assert.Equal(ScannerSessionPhase.Importing, restored.Phase);
            Assert.Equal(100m, restored.CostBrl); Assert.Equal(150m, restored.EstimatedValueBrl);
            Assert.Null(await store.Latest(Guid.NewGuid()));
            var imported = await importer.Import(restored); Assert.Equal(ScannerSessionPhase.Imported, imported.Phase); Assert.Equal(1, handler.CreatedCopies);
            await importer.Import(imported); Assert.Equal(2, handler.Requests);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new ScannerSessionImporter(client, store, () => Guid.NewGuid()).Import(imported));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class LostResponse : HttpMessageHandler
    {
        public int CreatedCopies { get; private set; }
        public int Requests { get; private set; }
        private string? _key;
        private string? _payload;
        private readonly Guid _itemId = Guid.NewGuid();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var key = Assert.Single(request.Headers.GetValues("Idempotency-Key")); var payload = await request.Content!.ReadAsStringAsync(ct);
            var body = await request.Content.ReadFromJsonAsync<AddCollectibleItemsRequest>(ct);
            Assert.Null(body!.AcquisitionPrice); Assert.Equal("UNKNOWN", body.Condition); Assert.Equal(1, body.Quantity);
            if (_key is null) { _key = key; _payload = payload; CreatedCopies++; throw new HttpRequestException("Response lost after commit"); }
            Assert.Equal(_key, key); Assert.Equal(_payload, payload);
            return new(HttpStatusCode.Created) { Content = JsonContent.Create(new AddCollectibleItemsResponse(Guid.NewGuid(), [new(_itemId, Guid.NewGuid())], 1)) };
        }
    }
}
