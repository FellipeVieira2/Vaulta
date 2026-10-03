using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Vaulta.Assets.Infrastructure;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Collection.Infrastructure;
using Vaulta.Identity.Domain;
using Vaulta.Identity.Infrastructure;
using Vaulta.Marketplace.Infrastructure;
using Vaulta.Orders.Infrastructure;
using Vaulta.Payments.Infrastructure;
using Vaulta.Reviews.Infrastructure;
using Vaulta.Shipping.Infrastructure;
using Vaulta.Wallets.Domain;
using Vaulta.Wallets.Infrastructure;
using Vaulta.Web.Api;
using Xunit;
using Vaulta.Vision.Infrastructure;

namespace Vaulta.Identity.IntegrationTests;

public sealed class DatabaseMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigratesAllModulesAndCanRepeatWithoutLosingData(bool existingFourModuleDatabase)
    {
        await using var postgres = await TestPostgresDatabase.Start();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Vaulta"] = postgres.ConnectionString,
                ["Jwt:Secret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Payouts:WorkerEnabled"] = "false",
                ["Refunds:WorkerEnabled"] = "false",
                ["Orders:ReleaseWorkerEnabled"] = "false",
                ["Orders:CollectionWorkerEnabled"] = "false",
                ["Marketplace:CollectionWorkerEnabled"] = "false",
                ["Database:ApplyMigrations"] = "false",
                ["Outbox:Enabled"] = "false"
            }));
        });

        Guid userId = Guid.Empty;
        if (existingFourModuleDatabase)
        {
            // Reproduce the production state before this fix, including an existing user/outbox.
            await using var legacyScope = factory.Services.CreateAsyncScope();
            var identity = legacyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await identity.Database.MigrateAsync();
            await legacyScope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
            await legacyScope.ServiceProvider.GetRequiredService<AssetsDbContext>().Database.MigrateAsync();
            await legacyScope.ServiceProvider.GetRequiredService<CollectionDbContext>().Database.MigrateAsync();
            userId = await AddUser(identity);
            DbContext[] legacyContexts =
            [
                identity,
                legacyScope.ServiceProvider.GetRequiredService<CatalogDbContext>(),
                legacyScope.ServiceProvider.GetRequiredService<AssetsDbContext>(),
                legacyScope.ServiceProvider.GetRequiredService<CollectionDbContext>()
            ];
            var expectedLegacyMigrations = legacyContexts.SelectMany(db => db.Database.GetMigrations()).Order().ToArray();
            Assert.Equal(expectedLegacyMigrations, (await identity.Database.GetAppliedMigrationsAsync()).Order().ToArray());
        }

        await DatabaseMigrations.ApplyAsync(factory.Services);

        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var identityDb = services.GetRequiredService<IdentityDbContext>();
        if (!existingFourModuleDatabase) userId = await AddUser(identityDb);

        var wallets = services.GetRequiredService<WalletsDbContext>();
        var wallet = Wallet.Create(userId, DateTimeOffset.UtcNow);
        wallet.Credit(12.50m, "migration-test", "Preserved on repeat", DateTimeOffset.UtcNow);
        wallets.Wallets.Add(wallet);
        await wallets.SaveChangesAsync();

        var historyBeforeRepeat = (await identityDb.Database.GetAppliedMigrationsAsync()).ToArray();
        var outboxBeforeRepeat = await identityDb.OutboxMessages.CountAsync();
        await DatabaseMigrations.ApplyAsync(factory.Services);

        Assert.Equal(historyBeforeRepeat, (await identityDb.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(outboxBeforeRepeat, await identityDb.OutboxMessages.CountAsync());
        Assert.Equal("migration@example.com", (await identityDb.Users.AsNoTracking().SingleAsync(x => x.Id == userId)).Email);
        Assert.Equal(12.50m, (await wallets.Wallets.AsNoTracking().SingleAsync(x => x.Id == wallet.Id)).Balance);
        Assert.Equal(12.50m, (await wallets.WalletLedgerEntries.AsNoTracking().SingleAsync(x => x.WalletId == wallet.Id)).Amount);

        DbContext[] contexts =
        [
            identityDb,
            services.GetRequiredService<CatalogDbContext>(),
            services.GetRequiredService<AssetsDbContext>(),
            services.GetRequiredService<CollectionDbContext>(),
            services.GetRequiredService<MarketplaceDbContext>(),
            services.GetRequiredService<OrdersDbContext>(),
            services.GetRequiredService<PaymentsDbContext>(),
            wallets,
            services.GetRequiredService<ShippingDbContext>(),
            services.GetRequiredService<ReviewsDbContext>(),
            services.GetRequiredService<VisionDbContext>()
        ];
        var expectedMigrations = contexts.SelectMany(db => db.Database.GetMigrations()).Order().ToArray();
        Assert.Equal(expectedMigrations, historyBeforeRepeat.Order().ToArray());

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT table_schema, table_name, column_name FROM information_schema.columns", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<(string Schema, string Table, string Column)>();
        while (await reader.ReadAsync()) columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));

        foreach (var db in contexts)
        {
            Assert.NotEmpty(db.Database.GetMigrations());
            Assert.False(db.Database.HasPendingModelChanges(), db.GetType().Name);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            foreach (var entity in db.Model.GetEntityTypes())
            {
                var table = entity.GetTableName()!;
                var schema = entity.GetSchema() ?? "public";
                var identifier = StoreObjectIdentifier.Table(table, schema);
                foreach (var property in entity.GetProperties())
                {
                    var column = property.GetColumnName(identifier);
                    if (column is not null) Assert.Contains((schema, table, column), columns);
                }
            }
        }
    }

    private static async Task<Guid> AddUser(IdentityDbContext db)
    {
        var user = User.Register("migration@example.com", "test-password-hash", "migrationuser", "Migration User", DateTimeOffset.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
