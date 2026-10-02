using Vaulta.Assets.Contracts;
using Vaulta.Catalog.Contracts;
using Vaulta.Collection.Contracts;
using Vaulta.Collection.Domain;
using Vaulta.Marketplace.Application;
using Vaulta.Marketplace.Domain;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Commerce.UnitTests;

public sealed class ListingPublicationRecoveryTests
{
    [Theory]
    [InlineData("deleted", "private", "collection-item", "image/jpeg", true)]
    [InlineData("pending", "private", "collection-item", "image/jpeg", true)]
    [InlineData("ready", "public", "collection-item", "image/jpeg", true)]
    [InlineData("ready", "private", "profile-avatar", "image/jpeg", true)]
    [InlineData("ready", "private", "collection-item", "application/pdf", true)]
    [InlineData("ready", "private", "collection-item", "image/jpeg", false)]
    public async Task UnavailableRealPhotoCannotBecomePublicAndMatchingReservationIsReleased(string status, string visibility, string purpose, string contentType, bool own)
    {
        var flow = new Flow();
        var id = flow.Draft.Photos.First().AssetId;
        flow.Photos.Access[id] = new(id, own ? flow.Draft.SellerUserId : Guid.NewGuid(), status, purpose, visibility, contentType);
        await Assert.ThrowsAsync<DomainException>(() => flow.Service.Publish(flow.Draft, default));
        Assert.Equal("cancelled", flow.Draft.Status); Assert.False(flow.Draft.IsActive); Assert.Null(flow.Collection.Item.ListedById);
    }

    [Fact]
    public async Task SuspendedSellerCannotResumePublication()
    {
        var flow = new Flow(); flow.Store.Seller.Suspend("Suspended after review", flow.Clock.UtcNow);
        await Assert.ThrowsAsync<ConflictException>(() => flow.Service.Publish(flow.Draft, default));
        Assert.Equal("cancelled", flow.Draft.Status); Assert.Null(flow.Collection.Item.ListedById);
    }

    [Fact]
    public async Task InactivePrintingCannotResumePublication()
    {
        var flow = new Flow(); flow.Catalog.Printing = flow.Catalog.Printing with { IsActive = false };
        await Assert.ThrowsAsync<ConflictException>(() => flow.Service.Publish(flow.Draft, default));
        Assert.Equal("cancelled", flow.Draft.Status); Assert.Null(flow.Collection.Item.ListedById);
    }

    [Fact]
    public async Task MissingPhotoCannotReleaseAnotherListingsReservation()
    {
        var flow = new Flow(); flow.Photos.Access.Clear();
        flow.Collection.Item.ReleaseListing(flow.Draft.Id, flow.Clock.UtcNow);
        var other = Guid.NewGuid(); flow.Collection.Item.ReserveForListing(other, flow.Clock.UtcNow);
        await Assert.ThrowsAsync<DomainException>(() => flow.Service.Publish(flow.Draft, default));
        Assert.Equal("cancelled", flow.Draft.Status); Assert.Equal(other, flow.Collection.Item.ListedById);
    }

    [Fact]
    public async Task ReadyPrivatePhotosAllowRecoveryAndRepeatDoesNotPublishAgain()
    {
        var flow = new Flow(); await flow.Service.Publish(flow.Draft, default);
        var version = flow.Draft.Version; await flow.Service.Publish(flow.Draft, default);
        Assert.Equal("active", flow.Draft.Status); Assert.Equal(flow.Draft.Id, flow.Collection.Item.ListedById); Assert.Equal(version, flow.Draft.Version);
    }

    [Fact]
    public async Task LegacyPublicationKeepsItsExistingNoPhotoBehavior()
    {
        var flow = new Flow(); flow.Collection.Item.ReleaseListing(flow.Draft.Id, flow.Clock.UtcNow);
        var legacy = Listing.Create(flow.Draft.SellerUserId, flow.Collection.Item.Id, flow.Draft.PrintingId, null, "NM", 90, null, flow.Clock.UtcNow);
        legacy.PreparePublication(flow.Clock.UtcNow); flow.Photos.Access.Clear();
        await flow.Service.Publish(legacy, default);
        Assert.True(legacy.IsActive); Assert.Equal(legacy.Id, flow.Collection.Item.ListedById);
    }

    private sealed class Flow
    {
        public TestClock Clock { get; } = new();
        public Listing Draft { get; }
        public DraftStore Store { get; }
        public HeldCollection Collection { get; }
        public PhotoAccess Photos { get; } = new();
        public PrintingAccess Catalog { get; }
        public ListingPublicationService Service { get; }
        public Flow()
        {
            var seller = Guid.NewGuid(); var item = CollectibleItem.Create(Guid.NewGuid(), seller, "NEAR_MINT", null, null, null, Clock.UtcNow);
            Draft = Listing.CreateDraft(seller, item.Id, Guid.NewGuid(), null, "NEAR_MINT", 90, null, "scan-1", "fingerprint", Clock.UtcNow);
            foreach (var type in new[] { "FRONT", "BACK" })
            {
                var asset = Guid.NewGuid(); Draft.AddDraftPhoto(asset, type, type == "FRONT" ? 0 : 1, Draft.Version, Clock.UtcNow);
                Photos.Access[asset] = new(asset, seller, "ready", "collection-item", "private", "image/jpeg");
            }
            Draft.PrepareDraftPublication("publish-1", Draft.Version, Clock.UtcNow); item.ReserveForListing(Draft.Id, Clock.UtcNow);
            Collection = new(item, Clock); Store = new(SellerProfile.Enable(seller, null, null, null, null, null, Clock.UtcNow));
            Catalog = new(new(Draft.PrintingId, Guid.NewGuid(), Guid.NewGuid(), "POKEMON", "Set", "Card", "1", "en", null, null, true));
            Service = new(Store, Collection, Clock, Photos, Catalog);
        }
    }

    private sealed class DraftStore(SellerProfile seller) : IMarketplaceStore
    {
        public SellerProfile Seller { get; } = seller;
        public Task<SellerProfile?> FindSellerProfile(Guid user, CancellationToken ct) => Task.FromResult<SellerProfile?>(Seller.UserId == user ? Seller : null);
        public Task Save(CancellationToken ct) => Task.CompletedTask;
        public Task RefreshListing(Listing listing, CancellationToken ct) => Task.CompletedTask;
        public Task<IAsyncDisposable> LockPublication(Guid id, CancellationToken ct) => Task.FromResult<IAsyncDisposable>(new Lease());
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
        public void AddSellerProfile(SellerProfile profile) => throw new NotSupportedException();
        public Task<Listing?> FindListing(Guid seller, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Listing?> FindActiveListing(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public void AddListing(Listing listing) => throw new NotSupportedException();
        public Task<Listing?> FindDraftByKey(Guid seller, string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<Listing> CreateDraft(Listing listing, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Listing>> CollectionSyncListings(int offset, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class HeldCollection(CollectibleItem item, IClock clock) : IMarketplaceCollection
    {
        public CollectibleItem Item { get; } = item;
        public Task ReserveListingItem(Listing listing, CancellationToken ct) { Item.ReserveForListing(listing.Id, clock.UtcNow); return Task.CompletedTask; }
        public Task ReleaseListingItem(Listing listing, CancellationToken ct) { Item.ReleaseListing(listing.Id, clock.UtcNow); return Task.CompletedTask; }
        public Task<CollectibleItemDto?> GetCollectibleItem(Guid user, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<MarketplaceItemIdentity?> GetItemIdentity(Guid user, Guid entry, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class PhotoAccess : IMarketplaceAssets
    {
        public Dictionary<Guid, CollectionAssetAccess> Access { get; } = [];
        public Task<CollectionAssetAccess?> GetAccess(Guid user, Guid id, CancellationToken ct) => Task.FromResult(Access.GetValueOrDefault(id));
        public Task<CollectionAssetUrl?> GetUrl(Guid user, Guid id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class PrintingAccess(CollectionPrintingDetails printing) : IMarketplaceCatalog
    {
        public CollectionPrintingDetails Printing { get; set; } = printing;
        public Task<CollectionPrintingDetails?> GetPrinting(Guid id, CancellationToken ct) => Task.FromResult<CollectionPrintingDetails?>(Printing.PrintingId == id ? Printing : null);
        public Task<CollectionVariantDetails?> GetVariant(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionPrintingDetails>> GetPrintings(IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionVariantDetails>> GetVariants(IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> SearchPrintingIds(string? query, string? game, CancellationToken ct) => throw new NotSupportedException();
    }
}
