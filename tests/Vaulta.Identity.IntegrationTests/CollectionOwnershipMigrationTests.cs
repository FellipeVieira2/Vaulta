using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Vaulta.Collection.Infrastructure;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

public sealed class CollectionOwnershipMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpgradeLocksLegacyUnitPreservesPrivateDataAndRejectsAmbiguousOwnership(bool ambiguous)
    {
        await using var postgres = await TestPostgresDatabase.Start();
        var options = new DbContextOptionsBuilder<CollectionDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        await using var db = new CollectionDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260926201932_AddCollectionIdempotencyKeys");
        var seller = Guid.NewGuid(); var entry = Guid.NewGuid(); var printing = Guid.NewGuid();
        var item = Guid.NewGuid(); var listing = Guid.NewGuid(); var duplicate = Guid.NewGuid(); var version = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA marketplace; CREATE TABLE marketplace.listings(id uuid PRIMARY KEY, seller_user_id uuid, collectible_item_id uuid, status text);");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO collection.collection_entries(id,user_id,printing_id,created_at,updated_at) VALUES ({entry},{seller},{printing},{now},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO collection.collectible_items(id,collection_entry_id,user_id,condition,status,notes,acquisition_amount,acquisition_currency,created_at,updated_at,version) VALUES ({item},{entry},{seller},'NEAR_MINT','ACTIVE','Legacy private note',30,'BRL',{now},{now},{version})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO marketplace.listings VALUES ({listing},{seller},{item},'active')");
        if (ambiguous)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO marketplace.listings VALUES ({duplicate},{seller},{item},'sold')");
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
            Assert.Contains("Multiple outstanding listings", error.MessageText);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM marketplace.listings WHERE id={duplicate}");
        }
        await migrator.MigrateAsync();
        var source = await db.Items.SingleAsync(x => x.Id == item);
        Assert.Equal(listing, source.ListedById); Assert.NotEqual(version, source.Version);
        Assert.Equal("Legacy private note", source.Notes); Assert.Equal(30m, source.AcquisitionAmount);
        Assert.Equal("ACTIVE", source.Status); Assert.Empty(await db.OwnershipTransfers.ToArrayAsync());
    }
}
