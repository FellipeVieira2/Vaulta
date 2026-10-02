namespace Vaulta.App.Core.Catalog;

public enum ScannerSessionPhase { Scanning, Completed, Importing, Imported }
public enum PackCostMode { NotProvided, PerPack, Total }

public sealed record SessionMarketValue(decimal AmountBrl, string Source, DateTimeOffset QuotedAt,
    decimal? OriginalAmount = null, string? OriginalCurrency = null, decimal? ExchangeRate = null, DateTimeOffset? ExchangeRateAt = null);
public sealed record ScannerSessionCard(Guid ScanId, Guid PrintingId, Guid? VariantId, string Name, string SetName,
    string CollectorNumber, string VariantName, string Condition, string? ArtworkUrl, SessionMarketValue? MarketValue,
    DateTimeOffset ScannedAt, Guid? ImportedItemId = null, bool ImportStarted = false,
    Guid? ListingDraftId = null, Guid? PublicationVersion = null, Guid? PublishedListingId = null);

/// <summary>A scan occurrence identifies a physical copy; printing IDs can repeat.</summary>
public sealed record ScannerSession(Guid Id, Guid OwnerId, DateTimeOffset StartedAt, ScannerSessionPhase Phase,
    int? PackCount, PackCostMode CostMode, decimal? EnteredCostBrl, IReadOnlyList<ScannerSessionCard> Cards)
{
    public string Currency => "BRL";
    public decimal? CostBrl => CostMode switch
    {
        PackCostMode.PerPack => EnteredCostBrl * PackCount,
        PackCostMode.Total => EnteredCostBrl,
        _ => null
    };
    public decimal EstimatedValueBrl => Cards.Sum(x => x.MarketValue?.AmountBrl ?? 0m);
    public int UnpricedCards => Cards.Count(x => x.MarketValue is null);
    public bool IsPartialValuation => UnpricedCards > 0;
    public decimal? EstimatedDifferenceBrl => CostBrl.HasValue ? EstimatedValueBrl - CostBrl.Value : null;
    public decimal? EstimatedDifferencePercent => CostBrl is > 0m ? EstimatedDifferenceBrl / CostBrl.Value * 100m : null;

    public static ScannerSession Start(Guid owner, DateTimeOffset now)
    {
        if (owner == Guid.Empty) throw new ArgumentException("Entre na sua conta para iniciar uma sessão.");
        return new(Guid.NewGuid(), owner, now, ScannerSessionPhase.Scanning, null, PackCostMode.NotProvided, null, []);
    }

    public ScannerSession ConfigureCost(int? packCount, PackCostMode mode, decimal? enteredCostBrl)
    {
        RequireScanning();
        if (packCount is < 1 or > 1000) throw new ArgumentException("Informe de 1 a 1.000 pacotes, ou deixe em branco.");
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Escolha como informar o custo.");
        if (mode == PackCostMode.NotProvided) return this with { PackCount = packCount, CostMode = mode, EnteredCostBrl = null };
        if (enteredCostBrl is null or < 0m or > 9999999.99m || decimal.Round(enteredCostBrl.Value, 2) != enteredCostBrl.Value)
            throw new ArgumentException("Informe um custo em reais com até duas casas decimais.");
        if (mode == PackCostMode.PerPack && packCount is null) throw new ArgumentException("Informe a quantidade de pacotes para calcular o custo total.");
        return this with { PackCount = packCount, CostMode = mode, EnteredCostBrl = enteredCostBrl };
    }

    public ScannerSession Add(ScannerSessionCard card)
    {
        RequireScanning();
        if (card.ScanId == Guid.Empty || card.PrintingId == Guid.Empty || card.VariantId == Guid.Empty || card.ImportedItemId is not null
            || string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.Condition))
            throw new ArgumentException("Confira a carta e a variante antes de somar à sessão.");
        if (card.MarketValue is { } value && (value.AmountBrl < 0m || decimal.Round(value.AmountBrl, 2) != value.AmountBrl
            || string.IsNullOrWhiteSpace(value.Source) || value.QuotedAt == default))
            throw new ArgumentException("A estimativa precisa ter valor válido em reais, fonte e data.");
        var existing = Cards.SingleOrDefault(x => x.ScanId == card.ScanId);
        if (existing is not null)
        {
            if (existing != card) throw new InvalidOperationException("Esta identificação já foi usada para outra carta.");
            return this;
        }
        return this with { Cards = Cards.Append(card).ToArray() };
    }

    public ScannerSession Remove(Guid scanId)
    {
        RequireScanning();
        if (Cards.Any(x => x.ScanId == scanId && (x.ImportStarted || x.ImportedItemId.HasValue)))
            throw new InvalidOperationException("Esta carta já iniciou a adição ao estoque. Gerencie a unidade na coleção.");
        return this with { Cards = Cards.Where(x => x.ScanId != scanId).ToArray() };
    }

    public ScannerSession BeginOccurrenceImport(Guid scanId)
    {
        var card = Cards.SingleOrDefault(x => x.ScanId == scanId) ?? throw new ArgumentException("Carta fora da sessão.");
        return this with { Cards = Cards.Select(x => x.ScanId == card.ScanId ? x with { ImportStarted = true } : x).ToArray() };
    }

    public ScannerSession RecordOccurrenceImported(Guid scanId, Guid itemId)
    {
        var card = Cards.SingleOrDefault(x => x.ScanId == scanId) ?? throw new ArgumentException("Carta fora da sessão.");
        if (!card.ImportStarted || itemId == Guid.Empty) throw new InvalidOperationException("Adição não iniciada.");
        if (card.ImportedItemId.HasValue && card.ImportedItemId != itemId) throw new InvalidOperationException("Carta já vinculada a outra unidade do estoque.");
        var cards = Cards.Select(x => x.ScanId == scanId ? x with { ImportedItemId = itemId } : x).ToArray();
        return this with { Cards = cards, Phase = Phase == ScannerSessionPhase.Importing && cards.All(x => x.ImportedItemId.HasValue) ? ScannerSessionPhase.Imported : Phase };
    }

    public ScannerSession RecordListingDraft(Guid scanId, Guid draftId) => ChangeOccurrence(scanId, card =>
    {
        if (!card.ImportedItemId.HasValue || draftId == Guid.Empty || card.ListingDraftId.HasValue && card.ListingDraftId != draftId)
            throw new InvalidOperationException("O rascunho não corresponde à unidade desta leitura.");
        return card with { ListingDraftId = draftId };
    });

    public ScannerSession BeginListingPublication(Guid scanId, Guid version) => ChangeOccurrence(scanId, card =>
    {
        if (!card.ListingDraftId.HasValue || version == Guid.Empty) throw new InvalidOperationException("Prepare o anúncio antes de publicar.");
        return card with { PublicationVersion = card.PublicationVersion ?? version };
    });

    public ScannerSession ResetListingPublication(Guid scanId) => ChangeOccurrence(scanId, card => card with { PublicationVersion = null });

    public ScannerSession RecordListingPublished(Guid scanId, Guid listingId) => ChangeOccurrence(scanId, card =>
    {
        if (card.ListingDraftId != listingId) throw new InvalidOperationException("Anúncio fora desta leitura.");
        return card with { PublishedListingId = listingId };
    });

    private ScannerSession ChangeOccurrence(Guid scanId, Func<ScannerSessionCard, ScannerSessionCard> change)
    {
        if (!Cards.Any(x => x.ScanId == scanId)) throw new ArgumentException("Carta fora da sessão.");
        return this with { Cards = Cards.Select(x => x.ScanId == scanId ? change(x) : x).ToArray() };
    }

    public ScannerSession Complete()
    {
        RequireScanning();
        if (Cards.Count == 0) throw new InvalidOperationException("Identifique ao menos uma carta antes de finalizar.");
        return this with { Phase = ScannerSessionPhase.Completed };
    }

    public ScannerSession Resume()
    {
        if (Phase != ScannerSessionPhase.Completed) throw new InvalidOperationException("Esta sessão não pode ser reaberta.");
        return this with { Phase = ScannerSessionPhase.Scanning };
    }

    public ScannerSession BeginImport()
    {
        if (Phase is not (ScannerSessionPhase.Completed or ScannerSessionPhase.Importing or ScannerSessionPhase.Imported))
            throw new InvalidOperationException("Finalize e revise a sessão antes de adicionar ao estoque.");
        return Phase == ScannerSessionPhase.Completed ? this with { Phase = ScannerSessionPhase.Importing } : this;
    }

    public ScannerSession RecordImported(Guid scanId, Guid itemId)
    {
        if (Phase != ScannerSessionPhase.Importing || itemId == Guid.Empty) throw new InvalidOperationException("Importação não iniciada.");
        var card = Cards.SingleOrDefault(x => x.ScanId == scanId) ?? throw new ArgumentException("Carta fora da sessão.");
        if (card.ImportedItemId.HasValue && card.ImportedItemId != itemId) throw new InvalidOperationException("Carta já vinculada a outra unidade do estoque.");
        var cards = Cards.Select(x => x.ScanId == scanId ? x with { ImportedItemId = itemId } : x).ToArray();
        return this with { Cards = cards, Phase = cards.All(x => x.ImportedItemId.HasValue) ? ScannerSessionPhase.Imported : Phase };
    }

    public bool IsHighlight(ScannerSessionCard card, decimal thresholdBrl = 100m) => card.MarketValue?.AmountBrl >= thresholdBrl;
    private void RequireScanning()
    {
        if (Phase != ScannerSessionPhase.Scanning) throw new InvalidOperationException("A sessão está finalizada. Reabra antes de alterar as cartas.");
    }
}
