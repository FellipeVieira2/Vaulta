using Microsoft.EntityFrameworkCore;
using Vaulta.Collection.Application;
using Vaulta.Collection.Domain;

namespace Vaulta.Collection.Infrastructure;

public sealed class CollectionCommerceStore(CollectionDbContext db) : ICollectionCommerceStore
{
    public Task<CollectionEntry?> FindEntryById(Guid userId, Guid entryId, CancellationToken ct) => db.Entries.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == entryId, ct);
    public Task<ItemOwnershipTransfer?> FindTransfer(Guid orderId, CancellationToken ct) => db.OwnershipTransfers.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
    public void AddTransfer(ItemOwnershipTransfer transfer) => db.OwnershipTransfers.Add(transfer);
}
