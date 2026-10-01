using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vaulta.Marketplace.Application;
using Vaulta.Marketplace.Domain;

namespace Vaulta.Marketplace.Infrastructure;

internal sealed class ListingCollectionWorker(IServiceScopeFactory scopes, ILogger<ListingCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var offset = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var listings = await scope.ServiceProvider.GetRequiredService<IMarketplaceStore>().CollectionSyncListings(offset, ct);
                foreach (var snapshot in listings)
                {
                    try
                    {
                        await using var repair = scopes.CreateAsyncScope();
                        var listing = await repair.ServiceProvider.GetRequiredService<IMarketplaceStore>().FindListing(snapshot.SellerUserId, snapshot.Id, ct);
                        if (listing?.Status == MarketplaceRules.PublishingStatus)
                            await repair.ServiceProvider.GetRequiredService<ListingPublicationService>().Publish(listing, ct);
                        else if (listing?.Status == MarketplaceRules.CancelledStatus)
                            await repair.ServiceProvider.GetRequiredService<IMarketplaceCollection>().ReleaseListingItem(listing, ct);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    { logger.LogWarning("Listing {ListingId} collection reconciliation will retry.", snapshot.Id); }
                }
                offset = listings.Count < 50 ? 0 : offset + 50;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            { logger.LogWarning("Listing collection reconciliation will retry."); }
        }
    }
}
