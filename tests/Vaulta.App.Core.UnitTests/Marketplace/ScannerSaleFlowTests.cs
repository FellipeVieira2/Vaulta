using System.Net;
using System.Net.Http.Json;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Collection;
using Vaulta.App.Core.Marketplace;
using Vaulta.Collection.Contracts;
using Vaulta.Marketplace.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Marketplace;

public sealed class ScannerSaleFlowTests
{
    [Fact]
    public async Task LostDraftResponseRetriesOriginalEmptyPriceWithoutNewUnit()
    {
        var f = new Fixture(); f.Drafts.LoseCreate = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Flow.Prepare(f.Session, f.ScanId));
        var recovered = await f.Flow.Prepare(f.Store.Value, f.ScanId);
        Assert.Equal(f.Drafts.Value.Id, recovered.Id); Assert.Equal(1, f.Items.Copies);
        Assert.Equal(2, f.Drafts.Creates.Count); Assert.Equal(f.Drafts.Creates[0], f.Drafts.Creates[1]);
        Assert.Null(f.Drafts.Creates[0].PriceBrl); Assert.Equal("UNKNOWN", f.Drafts.Creates[0].Condition);
        Assert.Equal(recovered.Id, f.Store.Value.Cards[0].ListingDraftId);
    }

    [Fact]
    public async Task PublishLostResponseReusesOriginalVersionAndKeyAfterReopen()
    {
        var f = new Fixture(); var draft = await f.Flow.Prepare(f.Session, f.ScanId);
        draft = f.Drafts.Value = draft with { Condition = "NEAR_MINT", PriceBrl = 75m };
        f.Drafts.LosePublish = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Flow.Publish(f.Store.Value, f.ScanId, draft));
        var reopened = f.NewFlow(); var published = await reopened.Publish(f.Store.Value, f.ScanId, draft with { Version = Guid.NewGuid() });
        Assert.Equal("active", published.Status); Assert.Equal(f.Drafts.Publishes[0], f.Drafts.Publishes[1]);
        Assert.Equal(published.Id, f.Store.Value.Cards[0].PublishedListingId);
        Assert.Equal("NEAR_MINT", f.Items.Item.Condition); Assert.Equal(new AcquisitionPrice(9m, "BRL"), f.Items.Item.AcquisitionPrice);
        Assert.Equal("private notes", f.Items.Item.Notes); Assert.Equal(1, f.Items.Updates);
    }

    [Fact]
    public async Task OtherAccountCannotPrepareOrPublishPrivateOccurrence()
    {
        var f = new Fixture(); f.Owner = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Flow.Prepare(f.Session, f.ScanId));
        Assert.Empty(f.Drafts.Creates); Assert.Equal(0, f.Items.Copies);
    }

    [Fact]
    public async Task WrongDraftOccurrenceIsRejectedBeforePublication()
    {
        var f = new Fixture(); var draft = await f.Flow.Prepare(f.Session, f.ScanId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Flow.Publish(f.Store.Value, f.ScanId, draft with { CollectibleItemId = Guid.NewGuid() }));
        Assert.Empty(f.Drafts.Publishes);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("sold")]
    public async Task AlreadyPublishedDraftRestoresReceiptWithoutPublishingTwice(string status)
    {
        var f = new Fixture(); var draft = await f.Flow.Prepare(f.Session, f.ScanId);
        var result = await f.Flow.Publish(f.Store.Value, f.ScanId, draft with { Status = status });
        Assert.Equal(result.Id, f.Store.Value.Cards[0].PublishedListingId); Assert.Empty(f.Drafts.Publishes);
    }

    private sealed class Fixture
    {
        public Guid? Owner;
        public Guid ScanId => Session.Cards[0].ScanId;
        public ScannerSession Session { get; }
        public MemoryStore Store { get; }
        public Items Items { get; } = new();
        public Drafts Drafts { get; } = new();
        public ScannerSaleFlow Flow { get; }
        public Fixture()
        {
            Owner = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            Session = ScannerSession.Start(Owner.Value, now).Add(new(Guid.NewGuid(), Guid.NewGuid(), null, "Card", "Set", "1", "Normal", "UNKNOWN", null, new(300m, "reference", now), now));
            Store = new(Session); Flow = NewFlow();
        }
        public ScannerSaleFlow NewFlow()
        {
            var client = new CollectionClient(new HttpClient(Items) { BaseAddress = new("https://example.test/") });
            return new(new(client, Store, () => Owner), client, Drafts, Store, () => Owner);
        }
    }
    private sealed class MemoryStore(ScannerSession value) : IScannerSessionStore
    {
        public ScannerSession Value { get; private set; } = value;
        public Task<ScannerSession?> Latest(Guid owner, CancellationToken ct = default) => Task.FromResult<ScannerSession?>(owner == Value.OwnerId ? Value : null);
        public Task Save(ScannerSession session, CancellationToken ct = default) { Value = session; return Task.CompletedTask; }
    }
    private sealed class Items : HttpMessageHandler
    {
        public int Copies { get; private set; }
        public int Updates { get; private set; }
        public CollectibleItemDto Item { get; private set; } = new(Guid.NewGuid(), Guid.NewGuid(), "UNKNOWN", new(9m, "BRL"), new(2025, 1, 1), "private notes", "owned", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid(), []);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object payload = Item;
            if (request.Method == HttpMethod.Post) { Copies++; payload = new AddCollectibleItemsResponse(Item.CollectionEntryId, [new(Item.Id, Item.Version)], 1); }
            if (request.Method == HttpMethod.Put)
            {
                var update = (await request.Content!.ReadFromJsonAsync<UpdateCollectibleItemRequest>(ct))!; Updates++;
                Item = Item with { Condition = update.Condition, AcquisitionPrice = update.AcquisitionPrice, Notes = update.Notes, Version = Guid.NewGuid() };
                payload = new { Item.Version };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(payload, payload.GetType()) };
        }
    }
    private sealed class Drafts : IListingDraftClient
    {
        public ListingDraftDto Value { get; set; } = null!;
        public bool LoseCreate; public bool LosePublish;
        public List<CreateListingDraftRequest> Creates { get; } = [];
        public List<PublishListingDraftRequest> Publishes { get; } = [];
        public Task<ListingDraftDto> CreateAsync(CreateListingDraftRequest request, CancellationToken ct = default)
        {
            Creates.Add(request); Value ??= new(Guid.NewGuid(), request.ClientDraftKey, Guid.Empty, request.CollectibleItemId, request.PrintingId, request.VariantId, request.Condition, request.PriceBrl, "BRL", "draft", request.Description, [], DateTimeOffset.UtcNow, Guid.NewGuid());
            if (LoseCreate) { LoseCreate = false; throw new HttpRequestException("Response lost"); } return Task.FromResult(Value);
        }
        public Task<ListingDraftDto> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Value);
        public Task<ListingDraftDto> PublishAsync(Guid id, PublishListingDraftRequest request, CancellationToken ct = default)
        {
            Publishes.Add(request); Value = Value with { Status = "active", Version = Guid.NewGuid() };
            if (LosePublish) { LosePublish = false; throw new HttpRequestException("Response lost"); } return Task.FromResult(Value);
        }
        public Task<ListingDraftDto> UpdateAsync(Guid id, UpdateListingDraftRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ListingDraftDto> AddPhotoAsync(Guid id, ListingDraftPhotoRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ListingDraftDto> RemovePhotoAsync(Guid id, Guid assetId, Guid version, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ListingDraftDto> CancelAsync(Guid id, Guid version, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
