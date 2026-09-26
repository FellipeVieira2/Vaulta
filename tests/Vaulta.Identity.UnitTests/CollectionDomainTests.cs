using Vaulta.Collection.Domain;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class CollectionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddingItemsCreatesDistinctPhysicalUnitsAndRaisesBatchEvent()
    {
        var entry = CollectionEntry.Create(Guid.NewGuid(), Guid.NewGuid(), null, Now);

        var items = entry.AddItems(3, "near mint", new Money(12.50m, "brl"), new DateOnly(2025, 12, 1), "  first edition  ", Now);

        Assert.Equal(3, items.Count);
        Assert.Equal(3, items.Select(item => item.Id).Distinct().Count());
        Assert.All(items, item =>
        {
            Assert.Equal(entry.Id, item.CollectionEntryId);
            Assert.Equal(entry.UserId, item.UserId);
            Assert.Equal("NEAR_MINT", item.Condition);
            Assert.Equal(new Money(12.50m, "BRL"), item.AcquisitionPrice);
            Assert.Equal("first edition", item.Notes);
        });
        var addedEvent = Assert.IsType<CollectibleItemsAddedDomainEvent>(Assert.Single(entry.DomainEvents));
        Assert.Equal(items.Select(item => item.Id), addedEvent.ItemIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void AddItemsRejectsQuantityOutsideSupportedRange(int quantity)
    {
        var entry = CollectionEntry.Create(Guid.NewGuid(), Guid.NewGuid(), null, Now);

        Assert.Throws<DomainException>(() => entry.AddItems(quantity, "NM", null, null, null, Now));
    }

    [Fact]
    public void UpdateRequiresCurrentVersionAndAdvancesIt()
    {
        var item = CreateItem();
        var oldVersion = item.Version;

        item.Update("LP", null, null, null, oldVersion, Now.AddMinutes(1));

        Assert.Equal("LIGHTLY_PLAYED", item.Condition);
        Assert.NotEqual(oldVersion, item.Version);
        Assert.Equal(Now.AddMinutes(1), item.UpdatedAt);
        Assert.IsType<CollectibleItemUpdatedDomainEvent>(Assert.Single(item.DomainEvents));
    }

    [Fact]
    public void UpdateRejectsStaleVersion()
    {
        var item = CreateItem();

        Assert.Throws<ConflictException>(() => item.Update("LP", null, null, null, Guid.NewGuid(), Now.AddMinutes(1)));
    }

    [Fact]
    public void RemovingItemIsSoftDeleteAndPreventsFurtherChanges()
    {
        var item = CreateItem();
        item.Remove(item.Version, Now.AddMinutes(1));

        Assert.False(item.IsActive);
        Assert.Equal(CollectionRules.RemovedStatus, item.Status);
        Assert.Equal(Now.AddMinutes(1), item.RemovedAt);
        Assert.IsType<CollectibleItemRemovedDomainEvent>(Assert.Single(item.DomainEvents));
        Assert.Throws<DomainException>(() => item.Update("NM", null, null, null, item.Version, Now.AddMinutes(2)));
    }

    [Fact]
    public void AssetMutationsMaintainOnePrimaryAssetAndRaiseEvents()
    {
        var item = CreateItem();
        var firstAssetId = Guid.NewGuid();
        var secondAssetId = Guid.NewGuid();
        item.AttachAsset(firstAssetId, "front", 0, Now.AddMinutes(1));
        item.AttachAsset(secondAssetId, "back", 1, Now.AddMinutes(2));

        Assert.True(item.Assets.Single(asset => asset.AssetId == firstAssetId).IsPrimary);
        item.SetPrimaryAsset(secondAssetId, Now.AddMinutes(3));
        Assert.True(item.Assets.Single(asset => asset.AssetId == secondAssetId).IsPrimary);
        Assert.Single(item.Assets, asset => asset.IsPrimary);

        item.RemoveAsset(secondAssetId, Now.AddMinutes(4));
        Assert.True(Assert.Single(item.Assets).IsPrimary);
    }

    [Theory]
    [InlineData("NM", "NEAR_MINT")]
    [InlineData("Near Mint", "NEAR_MINT")]
    [InlineData("near-mint", "NEAR_MINT")]
    [InlineData("NEAR_MINT", "NEAR_MINT")]
    [InlineData("M", "MINT")]
    [InlineData("mint", "MINT")]
    [InlineData("LP", "LIGHTLY_PLAYED")]
    [InlineData("Lightly Played", "LIGHTLY_PLAYED")]
    [InlineData("lightly-played", "LIGHTLY_PLAYED")]
    [InlineData("MP", "MODERATELY_PLAYED")]
    [InlineData("Moderately Played", "MODERATELY_PLAYED")]
    [InlineData("HP", "HEAVILY_PLAYED")]
    [InlineData("Heavily Played", "HEAVILY_PLAYED")]
    [InlineData("DMG", "DAMAGED")]
    [InlineData("damaged", "DAMAGED")]
    [InlineData("unknown", "UNKNOWN")]
    [InlineData("unspecified", "UNKNOWN")]
    public void ConditionNormalizesRecognizedAliasesToCanonicalCodes(string input, string expected) =>
        Assert.Equal(expected, CollectionRules.Condition(input));

    [Theory]
    [InlineData("PERFECT")]
    [InlineData("EXCELLENT")]
    [InlineData("EXCELENTE")]
    [InlineData("SUPER_BONITA")]
    [InlineData("QUASE_NOVA")]
    [InlineData("ABC")]
    [InlineData("")]
    [InlineData("   ")]
    public void ConditionRejectsUnrecognizedOrBlankCodes(string input) =>
        Assert.Throws<DomainException>(() => CollectionRules.Condition(input));

    [Fact]
    public void ConditionRejectsNull() =>
        Assert.Throws<DomainException>(() => CollectionRules.Condition(null!));

    private static CollectibleItem CreateItem() =>
        CollectibleItem.Create(Guid.NewGuid(), Guid.NewGuid(), "NM", null, null, null, Now);
}
