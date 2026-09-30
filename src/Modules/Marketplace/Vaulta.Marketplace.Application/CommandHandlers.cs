using FluentValidation;
using Vaulta.Assets.Contracts;
using Vaulta.Collection.Contracts;
using Vaulta.Marketplace.Application.Commands;
using Vaulta.Marketplace.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Marketplace.Application;

public sealed class MarketplaceCommandHandlers(
    IMarketplaceStore store,
    IMarketplaceCatalog catalog,
    IMarketplaceCollection collection,
    IMarketplaceAssets assets,
    IClock clock)
{
    public async Task<SellerProfileDto> Handle(EnableSellerCommand command, CancellationToken cancellationToken)
    {
        await new SellerProfileValidator().ValidateAndThrowAsync(command.Request, cancellationToken);
        var existing = await store.FindSellerProfile(command.UserId, cancellationToken);
        if (existing is not null)
            throw new ConflictException("Seller profile already exists for this user.");

        var now = clock.UtcNow;
        var profile = SellerProfile.Enable(
            command.UserId,
            command.Request.Bio,
            command.Request.Street,
            command.Request.City,
            command.Request.State,
            command.Request.ZipCode,
            now);
        store.AddSellerProfile(profile);
        await store.Save(cancellationToken);
        return MapProfile(profile);
    }

    public async Task<Guid> Handle(UpdateSellerProfileCommand command, CancellationToken cancellationToken)
    {
        await new SellerProfileValidator().ValidateAndThrowAsync(command.Request, cancellationToken);
        var profile = await RequireSellerProfile(command.UserId, cancellationToken);
        profile.Update(
            command.Request.Bio,
            command.Request.Street,
            command.Request.City,
            command.Request.State,
            command.Request.ZipCode,
            command.Version,
            clock.UtcNow);
        await store.Save(cancellationToken);
        return profile.Version;
    }

    public async Task<ListingDto> Handle(CreateListingCommand command, CancellationToken cancellationToken)
    {
        await new CreateListingValidator().ValidateAndThrowAsync(command.Request, cancellationToken);
        var seller = await RequireSellerProfile(command.UserId, cancellationToken);
        if (!seller.IsActive) throw new ConflictException("Seller profile is suspended.");

        var item = await collection.GetCollectibleItem(command.UserId, command.Request.CollectibleItemId, cancellationToken)
            ?? throw new NotFoundException("Collectible item not found or does not belong to this user.");
        if (item.Status != "active") throw new ConflictException("Only active collectible items can be listed.");

        var printing = await catalog.GetPrinting(command.Request.PrintingId, cancellationToken)
            ?? throw new NotFoundException("Printing not found.");
        if (!printing.IsActive) throw new ConflictException("Printing is no longer available in the catalog.");

        if (command.Request.VariantId is { } variantId)
        {
            var variant = await catalog.GetVariant(variantId, cancellationToken)
                ?? throw new NotFoundException("Variant not found.");
            if (variant.PrintingId != printing.PrintingId)
                throw new DomainException("Variant does not belong to the specified printing.");
            if (!variant.IsActive) throw new ConflictException("Variant is no longer available in the catalog.");
        }

        var now = clock.UtcNow;
        var listing = Listing.Create(
            command.UserId,
            command.Request.CollectibleItemId,
            command.Request.PrintingId,
            command.Request.VariantId,
            command.Request.Condition,
            command.Request.PriceBrl,
            command.Request.Description,
            now);
        store.AddListing(listing);
        await store.Save(cancellationToken);
        return await MapListing(listing, cancellationToken);
    }

    public async Task<Guid> Handle(UpdateListingCommand command, CancellationToken cancellationToken)
    {
        await new UpdateListingValidator().ValidateAndThrowAsync(command.Request, cancellationToken);
        var listing = await RequireListing(command.UserId, command.ListingId, cancellationToken);
        listing.Update(
            command.Request.Condition,
            command.Request.PriceBrl,
            command.Request.Description,
            command.Request.Version,
            clock.UtcNow);
        await store.Save(cancellationToken);
        return listing.Version;
    }

    public async Task Handle(CancelListingCommand command, CancellationToken cancellationToken)
    {
        var listing = await RequireListing(command.UserId, command.ListingId, cancellationToken);
        listing.Cancel(command.Version, clock.UtcNow);
        await store.Save(cancellationToken);
    }

    public async Task Handle(AddListingPhotoCommand command, CancellationToken cancellationToken)
    {
        await new ListingPhotoValidator().ValidateAndThrowAsync(command.Request, cancellationToken);
        var listing = await RequireListing(command.UserId, command.ListingId, cancellationToken);
        var asset = await assets.GetAccess(command.UserId, command.Request.AssetId, cancellationToken)
            ?? throw new NotFoundException("Asset not found.");
        ValidateAsset(asset, command.UserId);
        listing.AddPhoto(asset.AssetId, command.Request.Type, command.Request.SortOrder, clock.UtcNow);
        await store.Save(cancellationToken);
    }

    public async Task Handle(RemoveListingPhotoCommand command, CancellationToken cancellationToken)
    {
        var listing = await RequireListing(command.UserId, command.ListingId, cancellationToken);
        listing.RemovePhoto(command.AssetId, clock.UtcNow);
        await store.Save(cancellationToken);
    }

    private async Task<SellerProfile> RequireSellerProfile(Guid userId, CancellationToken cancellationToken) =>
        await store.FindSellerProfile(userId, cancellationToken)
        ?? throw new NotFoundException("Seller profile not found. Enable selling first.");

    private async Task<Listing> RequireListing(Guid userId, Guid listingId, CancellationToken cancellationToken) =>
        await store.FindListing(userId, listingId, cancellationToken)
        ?? throw new NotFoundException("Listing not found.");

    private static void ValidateAsset(CollectionAssetAccess asset, Guid ownerId)
    {
        if (asset.OwnerId != ownerId) throw new ForbiddenException("Asset does not belong to this user.");
        if (asset.Status != "ready" || asset.Visibility != "private" || !asset.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Only ready, private image assets can be attached to a listing.");
    }

    public static SellerProfileDto MapProfile(SellerProfile p) => new(
        p.UserId, p.Status, p.Bio, p.Street, p.City, p.State, p.ZipCode,
        p.AverageRating, p.TotalSales, p.CreatedAt, p.Version);

    internal async Task<ListingDto> MapListing(Listing l, CancellationToken ct)
    {
        var photos = new List<ListingPhotoDto>();
        foreach (var photo in l.Photos.OrderBy(p => p.SortOrder))
        {
            var url = await assets.GetUrl(photo.AssetId, ct);
            photos.Add(new ListingPhotoDto(photo.AssetId, photo.Type, photo.SortOrder, photo.IsPrimary,
                url?.Url ?? "", url?.ExpiresAt ?? DateTimeOffset.MinValue));
        }
        return new ListingDto(l.Id, l.SellerUserId, l.CollectibleItemId, l.PrintingId, l.VariantId,
            l.Condition, l.PriceBrl, l.Currency, l.Status, l.Description, photos, l.CreatedAt, l.Version);
    }
}

public sealed class SellerProfileValidator : AbstractValidator<SellerProfileRequest>
{
    public SellerProfileValidator()
    {
        RuleFor(x => x.Bio).MaximumLength(500);
        RuleFor(x => x.Street).MaximumLength(200);
        RuleFor(x => x.City).MaximumLength(200);
        RuleFor(x => x.State).MaximumLength(200);
        RuleFor(x => x.ZipCode).MaximumLength(10);
    }
}

public sealed class CreateListingValidator : AbstractValidator<CreateListingRequest>
{
    public CreateListingValidator()
    {
        RuleFor(x => x.CollectibleItemId).NotEmpty();
        RuleFor(x => x.PrintingId).NotEmpty();
        RuleFor(x => x.VariantId).Must(x => x is null || x != Guid.Empty);
        RuleFor(x => x.Condition).NotEmpty().MaximumLength(32);
        RuleFor(x => x.PriceBrl).InclusiveBetween(MarketplaceRules.MinPriceBrl, MarketplaceRules.MaxPriceBrl);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class UpdateListingValidator : AbstractValidator<UpdateListingRequest>
{
    public UpdateListingValidator()
    {
        RuleFor(x => x.Condition).NotEmpty().MaximumLength(32);
        RuleFor(x => x.PriceBrl).InclusiveBetween(MarketplaceRules.MinPriceBrl, MarketplaceRules.MaxPriceBrl);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Version).NotEmpty();
    }
}

public sealed class ListingPhotoValidator : AbstractValidator<ListingPhotoRequest>
{
    public ListingPhotoValidator()
    {
        RuleFor(x => x.AssetId).NotEmpty();
        RuleFor(x => x.Type).Must(x => x is "FRONT" or "BACK" or "DETAIL" or "OTHER");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 99);
    }
}