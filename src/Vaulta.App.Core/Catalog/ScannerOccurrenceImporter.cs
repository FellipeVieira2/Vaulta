using Vaulta.App.Core.Collection;
using Vaulta.Collection.Contracts;

namespace Vaulta.App.Core.Catalog;

/// <summary>Imports a physical occurrence without ending the camera session.</summary>
public sealed class ScannerOccurrenceImporter(ICollectionClient collection, IScannerSessionStore store, Func<Guid?> currentOwner)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ScannerSession> Import(ScannerSession session, Guid scanId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            RequireOwner(session);
            var saved = await store.Latest(session.OwnerId, ct);
            RequireOwner(session);
            if (saved is null || saved.Id != session.Id) throw new InvalidOperationException("Reabra a sessão salva antes de adicionar esta carta.");
            session = saved;
            var card = session.Cards.SingleOrDefault(x => x.ScanId == scanId) ?? throw new ArgumentException("Carta fora da sessão.");
            if (card.PrintingId == Guid.Empty) throw new InvalidOperationException("Resolva a edição no catálogo antes de adicionar ao estoque ou vender.");
            if (card.ImportedItemId.HasValue) return session;
            session = session.BeginOccurrenceImport(scanId);
            await store.Save(session, ct); // Freeze before HTTP; a lost response retries the exact same payload.
            RequireOwner(session);
            var request = new AddCollectibleItemsRequest(card.PrintingId, card.VariantId, 1, card.Condition, null,
                DateOnly.FromDateTime(session.StartedAt.UtcDateTime), ScannerValuation.CertificationNotes(card.VisualIdentification));
            var receipt = await collection.AddItemsAsync(request, $"scanner-session-{session.Id:N}-{scanId:N}", ct);
            RequireOwner(session);
            session = session.RecordOccurrenceImported(scanId, receipt.CreatedItems.Single().Id);
            await store.Save(session, ct);
            RequireOwner(session);
            return session;
        }
        finally { _gate.Release(); }
    }

    private void RequireOwner(ScannerSession session)
    {
        if (session.OwnerId != currentOwner()) throw new InvalidOperationException("Entre na conta que iniciou esta sessão para adicionar a carta.");
    }
}
