using System.Data;
using Microsoft.EntityFrameworkCore;
using Vaulta.Collection.Application;
using Vaulta.Collection.Domain;
using Npgsql;

namespace Vaulta.Collection.Infrastructure;

internal sealed class CollectionStore(CollectionDbContext db) : ICollectionStore
{
    public async Task LockEntryIdentity(Guid userId, Guid printingId, Guid? variantId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var lockKey = variantId?.ToString("N") ?? "none";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({userId.ToString("N") + printingId.ToString("N") + lockKey}, 0))", cancellationToken);
    }

    public Task<CollectionEntry?> FindEntry(Guid userId, Guid printingId, Guid? variantId, CancellationToken cancellationToken) =>
        db.Entries.SingleOrDefaultAsync(x => x.UserId == userId && x.PrintingId == printingId && x.VariantId == variantId, cancellationToken);

    public void AddEntry(CollectionEntry entry) => db.Entries.Add(entry);
    public void AddItems(IEnumerable<CollectibleItem> items) => db.Items.AddRange(items);
    public Task<CollectibleItem?> FindItem(Guid userId, Guid itemId, CancellationToken cancellationToken) =>
        db.Items.Include(x => x.Assets).SingleOrDefaultAsync(x => x.UserId == userId && x.Id == itemId, cancellationToken);

    public Task<IdempotencyRecord?> FindIdempotencyRecord(Guid userId, string operation, string idempotencyKey, CancellationToken cancellationToken) =>
        db.IdempotencyKeys.AsNoTracking().Where(x => x.UserId == userId && x.Operation == operation && x.IdempotencyKey == idempotencyKey)
            .Select(x => new IdempotencyRecord(x.Id, x.UserId, x.Operation, x.IdempotencyKey, x.RequestHash, x.ResponseStatus, x.ResponsePayload, x.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public void AddIdempotencyRecord(IdempotencyRecord record) => db.IdempotencyKeys.Add(new CollectionIdempotencyKey
    {
        Id = record.Id, UserId = record.UserId, Operation = record.Operation, IdempotencyKey = record.IdempotencyKey,
        RequestHash = record.RequestHash, ResponseStatus = record.ResponseStatus, ResponsePayload = record.ResponsePayload, CreatedAt = record.CreatedAt
    });

    public async Task Save(CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        if (db.Database.CurrentTransaction is not null) await db.Database.CommitTransactionAsync(cancellationToken);
    }
}
