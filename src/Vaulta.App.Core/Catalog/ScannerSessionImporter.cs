using Vaulta.App.Core.Collection;
using Vaulta.Collection.Contracts;

namespace Vaulta.App.Core.Catalog;

public sealed class ScannerSessionImporter(ICollectionClient collection, IScannerSessionStore store, Func<Guid?> currentOwner)
{
    public async Task<ScannerSession> Import(ScannerSession session, CancellationToken ct = default)
    {
        RequireOwner(session);
        session = session.BeginImport();
        await store.Save(session, ct); // freeze before the first request; retry payloads must remain identical
        foreach (var card in session.Cards.Where(x => !x.ImportedItemId.HasValue).ToArray())
        {
            RequireOwner(session); ct.ThrowIfCancellationRequested();
            // Pack expense belongs to the session. A card's purchase price is unknown;
            // its market estimate is never persisted as an acquisition price.
            var request = new AddCollectibleItemsRequest(card.PrintingId, card.VariantId, 1, card.Condition, null,
                DateOnly.FromDateTime(session.StartedAt.UtcDateTime), ScannerValuation.CertificationNotes(card.VisualIdentification));
            var result = await collection.AddItemsAsync(request, $"scanner-session-{session.Id:N}-{card.ScanId:N}", ct);
            RequireOwner(session);
            session = session.RecordImported(card.ScanId, result.CreatedItems.Single().Id);
            await store.Save(session, ct);
        }
        return session;
    }
    private void RequireOwner(ScannerSession session)
    {
        if (session.OwnerId != currentOwner()) throw new InvalidOperationException("Entre na conta que iniciou esta sessão para adicionar as cartas.");
    }
}
