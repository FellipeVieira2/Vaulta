using FluentValidation;
using Vaulta.Assets.Contracts;
using Vaulta.Catalog.Contracts;
using Vaulta.Collection.Application.Commands;
using Vaulta.Collection.Application.Queries;
using Vaulta.Collection.Contracts;
using Vaulta.Collection.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Collection.Application;

public sealed class CollectionCommandHandlers(ICollectionStore store, ICollectionCatalog catalog, ICollectionAssets assets, IClock clock)
{
    public async Task<AddCollectibleItemsResponse> Handle(AddCollectibleItemsCommand command, CancellationToken cancellationToken)
    {
        await new AddCollectibleItemsValidator().ValidateAndThrowAsync(command.Request, cancellationToken);
        var request = command.Request;
        var printing = await catalog.GetPrinting(request.PrintingId, cancellationToken) ?? throw new NotFoundException("Printing not found.");
        if (request.VariantId is { } variantId)
        {
            var variant = await catalog.GetVariant(variantId, cancellationToken) ?? throw new NotFoundException("Variant not found.");
            if (variant.PrintingId != printing.PrintingId) throw new DomainException("Variant does not belong to the specified printing.");
        }

        var now = clock.UtcNow;
        await store.LockEntryIdentity(command.UserId, request.PrintingId, request.VariantId, cancellationToken);
        var entry = await store.FindEntry(command.UserId, request.PrintingId, request.VariantId, cancellationToken);
        if (entry is null)
        {
            entry = CollectionEntry.Create(command.UserId, request.PrintingId, request.VariantId, now);
            store.AddEntry(entry);
        }
        var price = request.AcquisitionPrice is null ? null : new Money(request.AcquisitionPrice.Amount, request.AcquisitionPrice.Currency);
        var items = entry.AddItems(request.Quantity, request.Condition, price, request.AcquisitionDate, request.Notes, now);
        store.AddItems(items);
        await store.Save(cancellationToken);
        return new AddCollectibleItemsResponse(entry.Id, items.Select(x => new CollectibleItemCreatedDto(x.Id, x.Version)).ToArray(), items.Count);
    }

    public async Task<Guid> Handle(UpdateCollectibleItemCommand command, CancellationToken cancellationToken)
    {
        await new UpdateCollectibleItemValidator().ValidateAndThrowAsync(command.Request, cancellationToken);
        var item = await RequireItem(command.UserId, command.ItemId, cancellationToken);
        var request = command.Request;
        var price = request.AcquisitionPrice is null ? null : new Money(request.AcquisitionPrice.Amount, request.AcquisitionPrice.Currency);
        item.Update(request.Condition, price, request.AcquisitionDate, request.Notes, request.Version, clock.UtcNow);
        await store.Save(cancellationToken);
        return item.Version;
    }

    public async Task Handle(RemoveCollectibleItemCommand command, CancellationToken cancellationToken)
    {
        var item = await RequireItem(command.UserId, command.ItemId, cancellationToken);
        item.Remove(command.Version, clock.UtcNow);
        await store.Save(cancellationToken);
    }

    public async Task Handle(AttachCollectibleItemAssetCommand command, CancellationToken cancellationToken)
    {
        await new AttachAssetValidator().ValidateAndThrowAsync(command.Request, cancellationToken);
        var item = await RequireItem(command.UserId, command.ItemId, cancellationToken);
        var asset = await assets.GetAccess(command.UserId, command.Request.AssetId, cancellationToken)
            ?? throw new NotFoundException("Asset not found.");
        ValidateAsset(asset, command.UserId);
        item.AttachAsset(asset.AssetId, command.Request.Type, command.Request.SortOrder, clock.UtcNow);
        await store.Save(cancellationToken);
    }

    public async Task Handle(RemoveCollectibleItemAssetCommand command, CancellationToken cancellationToken)
    {
        var item = await RequireItem(command.UserId, command.ItemId, cancellationToken);
        item.RemoveAsset(command.AssetId, clock.UtcNow);
        await store.Save(cancellationToken);
    }

    public async Task Handle(SetPrimaryCollectibleItemAssetCommand command, CancellationToken cancellationToken)
    {
        var item = await RequireItem(command.UserId, command.ItemId, cancellationToken);
        item.SetPrimaryAsset(command.AssetId, clock.UtcNow);
        await store.Save(cancellationToken);
    }

    private async Task<CollectibleItem> RequireItem(Guid userId, Guid itemId, CancellationToken cancellationToken) =>
        await store.FindItem(userId, itemId, cancellationToken) ?? throw new NotFoundException("Collectible item not found.");

    private static void ValidateAsset(CollectionAssetAccess asset, Guid ownerId)
    {
        if (asset.OwnerId != ownerId) throw new ForbiddenException("Asset does not belong to this user.");
        if (asset.Status != "ready" || asset.Visibility != "private" || !asset.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Only ready, private image assets can be attached to a collectible item.");
    }
}

public sealed class CollectionQueryHandlers(ICollectionQueries queries)
{
    public Task<CollectionPageDto> Handle(GetMyCollectionQuery query, CancellationToken cancellationToken) => queries.GetMyCollection(query.UserId, query.Filter, cancellationToken);
    public async Task<CollectionEntryDetailsDto> Handle(GetCollectionEntryQuery query, CancellationToken cancellationToken) =>
        await queries.GetEntry(query.UserId, query.EntryId, query.ItemsPage, query.ItemsPageSize, cancellationToken)
        ?? throw new NotFoundException("Collection entry not found.");
    public async Task<CollectibleItemDto> Handle(GetCollectibleItemQuery query, CancellationToken cancellationToken) =>
        await queries.GetItem(query.UserId, query.ItemId, cancellationToken) ?? throw new NotFoundException("Collectible item not found.");
    public Task<CollectionSummaryDto> Handle(GetCollectionSummaryQuery query, CancellationToken cancellationToken) => queries.GetSummary(query.UserId, cancellationToken);
}

public sealed class AddCollectibleItemsValidator : AbstractValidator<AddCollectibleItemsRequest>
{
    public AddCollectibleItemsValidator()
    {
        RuleFor(x => x.PrintingId).NotEmpty();
        RuleFor(x => x.VariantId).Must(x => x is null || x != Guid.Empty);
        RuleFor(x => x.Quantity).InclusiveBetween(1, 100);
        RuleFor(x => x.Condition).NotEmpty().MaximumLength(32);
        RuleFor(x => x.AcquisitionPrice).Must(x => x is null || x.Amount >= 0);
        RuleFor(x => x.AcquisitionPrice).Must(x => x is null || x.Currency.Length == 3);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class UpdateCollectibleItemValidator : AbstractValidator<UpdateCollectibleItemRequest>
{
    public UpdateCollectibleItemValidator()
    {
        RuleFor(x => x.Condition).NotEmpty().MaximumLength(32);
        RuleFor(x => x.AcquisitionPrice).Must(x => x is null || x.Amount >= 0);
        RuleFor(x => x.AcquisitionPrice).Must(x => x is null || x.Currency.Length == 3);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Version).NotEmpty();
    }
}

public sealed class AttachAssetValidator : AbstractValidator<AttachCollectibleItemAssetRequest>
{
    public AttachAssetValidator()
    {
        RuleFor(x => x.AssetId).NotEmpty();
        RuleFor(x => x.Type).Must(x => x is "FRONT" or "BACK" or "DETAIL" or "OTHER");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 99);
    }
}
