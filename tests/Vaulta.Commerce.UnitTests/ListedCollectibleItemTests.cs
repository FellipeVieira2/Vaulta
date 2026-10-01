using Vaulta.Collection.Domain;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Commerce.UnitTests;

public sealed class ListedCollectibleItemTests
{
    [Fact]
    public void ListingHoldProtectsEveryMutationAndStaleReleaseCannotUnlockNewListing()
    {
        var now = new TestClock().UtcNow;
        var item = CollectibleItem.Create(Guid.NewGuid(), Guid.NewGuid(), "NM", null, null, null, now);
        var asset = Guid.NewGuid(); item.AttachAsset(asset, "FRONT", 0, now);
        var listing = Guid.NewGuid(); item.ReserveForListing(listing, now);
        var version = item.Version;
        Assert.Throws<ConflictException>(() => item.Update("LP", null, null, null, version, now));
        Assert.Throws<ConflictException>(() => item.Remove(version, now));
        Assert.Throws<ConflictException>(() => item.AttachAsset(Guid.NewGuid(), "BACK", 1, now));
        Assert.Throws<ConflictException>(() => item.RemoveAsset(asset, now));
        Assert.Throws<ConflictException>(() => item.SetPrimaryAsset(asset, now));
        item.ReserveForListing(listing, now); Assert.Equal(version, item.Version);
        item.ReleaseListing(listing, now); var replacement = Guid.NewGuid(); item.ReserveForListing(replacement, now);
        item.ReleaseListing(listing, now); Assert.Equal(replacement, item.ListedById);
    }
}
