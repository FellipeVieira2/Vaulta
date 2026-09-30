using Microsoft.EntityFrameworkCore;
using Vaulta.Assets.Infrastructure;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Collection.Infrastructure;
using Vaulta.Identity.Infrastructure;
using Vaulta.Marketplace.Infrastructure;
using Vaulta.Orders.Infrastructure;
using Vaulta.Payments.Infrastructure;
using Vaulta.Reviews.Infrastructure;
using Vaulta.Shipping.Infrastructure;
using Vaulta.Wallets.Infrastructure;

namespace Vaulta.Web.Api;

public static class DatabaseMigrations
{
    public static async Task ApplyAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        DbContext[] contexts =
        [
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
            scope.ServiceProvider.GetRequiredService<CatalogDbContext>(),
            scope.ServiceProvider.GetRequiredService<AssetsDbContext>(),
            scope.ServiceProvider.GetRequiredService<CollectionDbContext>(),
            scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>(),
            scope.ServiceProvider.GetRequiredService<OrdersDbContext>(),
            scope.ServiceProvider.GetRequiredService<PaymentsDbContext>(),
            scope.ServiceProvider.GetRequiredService<WalletsDbContext>(),
            scope.ServiceProvider.GetRequiredService<ShippingDbContext>(),
            scope.ServiceProvider.GetRequiredService<ReviewsDbContext>()
        ];

        // Identity creates the shared outbox first. Module migrations exclude that table.
        // Keep execution sequential because every context shares the same database/history.
        foreach (var context in contexts)
            await context.Database.MigrateAsync(cancellationToken);
    }
}
