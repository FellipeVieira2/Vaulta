using Microsoft.Extensions.DependencyInjection;
using Vaulta.Collection.Domain;
using Vaulta.Collection.Infrastructure;
using Vaulta.Marketplace.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.IntegrationTests;

internal static class CollectionCommerceSeed
{
    public static async Task<Listing> Listing(IServiceProvider services, Guid seller, Guid? printingId = null, bool privatePhoto = false)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = CollectionEntry.Create(seller, printingId ?? Guid.NewGuid(), null, now);
        var item = entry.AddItems(1, "NM", new Money(30m, "BRL"), DateOnly.FromDateTime(now.UtcDateTime), "Seller private notes", now).Single();
        var listing = Vaulta.Marketplace.Domain.Listing.Create(seller, item.Id, entry.PrintingId, null, "NM", 100, null, now);
        if (privatePhoto) item.AttachAsset(Guid.NewGuid(), "FRONT", 0, now);
        item.ReserveForListing(listing.Id, now);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
        db.Entries.Add(entry); db.Items.Add(item); await db.SaveChangesAsync();
        return listing;
    }
}
