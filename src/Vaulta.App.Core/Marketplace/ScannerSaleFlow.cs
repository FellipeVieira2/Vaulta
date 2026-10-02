using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Collection;
using Vaulta.App.Core.Http;
using Vaulta.Marketplace.Contracts;

namespace Vaulta.App.Core.Marketplace;

public sealed class ScannerSaleFlow(ScannerOccurrenceImporter importer, ICollectionClient collection,
    IListingDraftClient drafts, IScannerSessionStore store, Func<Guid?> currentOwner)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ListingDraftDto> Prepare(ScannerSession session, Guid scanId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            session = await importer.Import(session, scanId, ct); RequireOwner(session);
            var card = Card(session, scanId);
            var draft = card.ListingDraftId is { } id ? await drafts.GetAsync(id, ct)
                : await drafts.CreateAsync(new($"scanner-{session.Id:N}-{scanId:N}", card.ImportedItemId!.Value,
                    card.PrintingId, card.VariantId, card.Condition, null, null), ct);
            RequireOwner(session);
            if (draft.CollectibleItemId != card.ImportedItemId || draft.PrintingId != card.PrintingId || draft.VariantId != card.VariantId)
                throw new InvalidOperationException("O rascunho recebido não corresponde a esta carta.");
            session = session.RecordListingDraft(scanId, draft.Id);
            if (draft.Status is "active" or "sold") session = session.RecordListingPublished(scanId, draft.Id);
            await store.Save(session, ct); RequireOwner(session); return draft;
        }
        finally { _gate.Release(); }
    }

    public async Task<ListingDraftDto> Publish(ScannerSession session, Guid scanId, ListingDraftDto draft, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            session = await Read(session, ct); var card = Card(session, scanId);
            if (draft.Id != card.ListingDraftId || draft.CollectibleItemId != card.ImportedItemId)
                throw new InvalidOperationException("O rascunho não corresponde a esta leitura.");
            if (draft.Status is "active" or "sold")
            {
                await store.Save(session.RecordListingPublished(scanId, draft.Id), ct); RequireOwner(session); return draft;
            }
            if (!card.PublicationVersion.HasValue)
            {
                var item = await collection.GetItemAsync(draft.CollectibleItemId, ct); RequireOwner(session);
                if (item.Condition != draft.Condition)
                    await collection.UpdateItemAsync(item.Id, new(draft.Condition, item.AcquisitionPrice, item.AcquisitionDate, item.Notes, item.Version), ct);
                RequireOwner(session);
                session = session.BeginListingPublication(scanId, draft.Version);
                await store.Save(session, ct); RequireOwner(session);
            }
            try
            {
                var published = await drafts.PublishAsync(draft.Id,
                    new($"scanner-publish-{draft.Id:N}", Card(session, scanId).PublicationVersion!.Value), ct);
                RequireOwner(session);
                if (published.Status is "active" or "sold") await store.Save(session.RecordListingPublished(scanId, published.Id), ct);
                RequireOwner(session); return published;
            }
            catch (ApiException)
            {
                // Definitive rejection can leave a draft editable; uncertain transport failures retain the frozen receipt.
                var current = await drafts.GetAsync(draft.Id, ct); RequireOwner(session);
                if (current.Status == "draft") await store.Save(session.ResetListingPublication(scanId), ct);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<ScannerSession> Read(ScannerSession session, CancellationToken ct)
    {
        RequireOwner(session); var saved = await store.Latest(session.OwnerId, ct); RequireOwner(session);
        if (saved?.Id != session.Id) throw new InvalidOperationException("Reabra a sessão salva antes de publicar.");
        return saved;
    }
    private static ScannerSessionCard Card(ScannerSession session, Guid scanId) => session.Cards.SingleOrDefault(x => x.ScanId == scanId) ?? throw new ArgumentException("Carta fora da sessão.");
    private void RequireOwner(ScannerSession session)
    {
        if (session.OwnerId != currentOwner()) throw new InvalidOperationException("Entre na conta que iniciou esta sessão para vender a carta.");
    }
}
