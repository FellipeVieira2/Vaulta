using Vaulta.Marketplace.Domain;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Commerce.UnitTests;

public sealed class ListingDraftTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnpricedDraftHasNoPublicEventAndCanBeCancelledWithoutPublication()
    {
        var draft = Draft();
        Assert.Equal("draft", draft.Status); Assert.False(draft.IsActive); Assert.Empty(draft.DomainEvents);
        Assert.Equal(0, draft.PriceBrl);
        draft.CancelDraft(draft.Version, Now);
        Assert.Equal("cancelled", draft.Status); Assert.Empty(draft.DomainEvents);
    }

    [Fact]
    public void DraftEditsAndPhotosRejectStaleVersionAndCannotAlterPublishingPayload()
    {
        var draft = Draft(); var initialVersion = draft.Version;
        draft.UpdateDraft("NEAR_MINT", 90, "Unit photos", initialVersion, Now);
        Assert.Throws<ConflictException>(() => draft.UpdateDraft("NEAR_MINT", 100, null, initialVersion, Now));
        Assert.Throws<ConflictException>(() => draft.AddDraftPhoto(Guid.NewGuid(), "FRONT", 0, initialVersion, Now));
        draft.AddDraftPhoto(Guid.NewGuid(), "FRONT", 0, draft.Version, Now);
        draft.AddDraftPhoto(Guid.NewGuid(), "BACK", 1, draft.Version, Now);
        var reviewed = draft.Version; draft.PrepareDraftPublication("publish-1", reviewed, Now);
        Assert.Equal("publishing", draft.Status);
        Assert.Throws<ConflictException>(() => draft.UpdateDraft("NEAR_MINT", 95, null, draft.Version, Now));
        Assert.Throws<ConflictException>(() => draft.RemoveDraftPhoto(draft.Photos.First().AssetId, draft.Version, Now));
    }

    [Fact]
    public void PublicationRequiresCanonicalConditionManualPriceAndBothSides()
    {
        var draft = Draft();
        Assert.Throws<DomainException>(() => draft.PrepareDraftPublication("publish-1", draft.Version, Now));
        draft.UpdateDraft("UNKNOWN", 90, null, draft.Version, Now);
        Assert.Throws<DomainException>(() => draft.PrepareDraftPublication("publish-1", draft.Version, Now));
        Assert.Throws<DomainException>(() => draft.UpdateDraft("invented", 90, null, draft.Version, Now));
        draft.UpdateDraft("NEAR_MINT", 90, null, draft.Version, Now);
        draft.AddDraftPhoto(Guid.NewGuid(), "FRONT", 0, draft.Version, Now);
        Assert.Throws<DomainException>(() => draft.PrepareDraftPublication("publish-1", draft.Version, Now));
        draft.AddDraftPhoto(Guid.NewGuid(), "BACK", 1, draft.Version, Now);
        draft.PrepareDraftPublication("publish-1", draft.Version, Now);
        Assert.Equal("publishing", draft.Status);
    }

    [Fact]
    public void PublishRetryUsesOriginalReviewVersionAndRejectsDifferentPayload()
    {
        var draft = Draft(); draft.UpdateDraft("NEAR_MINT", 90, null, draft.Version, Now);
        draft.AddDraftPhoto(Guid.NewGuid(), "FRONT", 0, draft.Version, Now); draft.AddDraftPhoto(Guid.NewGuid(), "BACK", 1, draft.Version, Now);
        var reviewed = draft.Version; draft.PrepareDraftPublication("publish-1", reviewed, Now); var publishingVersion = draft.Version;
        draft.PrepareDraftPublication("publish-1", reviewed, Now); Assert.Equal(publishingVersion, draft.Version);
        draft.Publish(Now); var activeVersion = draft.Version;
        draft.PrepareDraftPublication("publish-1", reviewed, Now); Assert.Equal(activeVersion, draft.Version); Assert.True(draft.IsActive);
        Assert.Throws<ConflictException>(() => draft.PrepareDraftPublication("other", reviewed, Now));
        Assert.Throws<ConflictException>(() => draft.PrepareDraftPublication("publish-1", activeVersion, Now));
    }

    [Fact]
    public void LegacyPhotoAndCancelCommandsCannotMutateDraftBeforeReview()
    {
        var draft = Draft();
        Assert.Throws<DomainException>(() => draft.AddPhoto(Guid.NewGuid(), "FRONT", 0, Now));
        Assert.Throws<ConflictException>(() => draft.Cancel(draft.Version, Now));
    }

    private static Listing Draft() => Listing.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "UNKNOWN", null, null, "scan-1", "fingerprint", Now);
}
