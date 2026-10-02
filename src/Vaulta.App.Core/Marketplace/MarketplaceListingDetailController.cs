using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Http;
using Vaulta.Marketplace.Contracts;

namespace Vaulta.App.Core.Marketplace;

public sealed record MarketplaceListingDetailState(Guid? ListingId = null, ListingDto? Listing = null,
    IReadOnlyList<ListingDto>? ComparisonItems = null, bool IsLoading = false, bool IsComparing = false,
    bool NotFound = false, string? Message = null, string? MetadataMessage = null, string? ComparisonMessage = null,
    int ComparisonPage = 0, bool HasMoreComparisons = false)
{
    public IReadOnlyList<ListingDto> Comparisons => ComparisonItems ?? [];
    public bool CanBuy => Listing is { Status: "active" } && !IsLoading;
}

/// <summary>Public detail and compatible offers, without checkout or private seller profile access.</summary>
public sealed class MarketplaceListingDetailController(IMarketplaceClient marketplace, ICatalogClient catalog) : IDisposable
{
    private enum LoadMode { Detail, Comparisons, MoreComparisons }
    private readonly object _gate = new();
    private MarketplaceListingDetailState _state = new();
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _retryMore;
    private bool _disposed;
    public MarketplaceListingDetailState State { get { lock (_gate) return _state; } }
    public event EventHandler? StateChanged;

    public Task LoadAsync(Guid listingId, CancellationToken cancellationToken = default) => RunAsync(listingId, LoadMode.Detail, cancellationToken);
    public Task LoadMoreComparisonsAsync(CancellationToken cancellationToken = default) => RunAsync(null, LoadMode.MoreComparisons, cancellationToken);
    public Task RetryComparisonsAsync(CancellationToken cancellationToken = default)
    {
        bool more;
        lock (_gate) more = _retryMore;
        return RunAsync(null, more ? LoadMode.MoreComparisons : LoadMode.Comparisons, cancellationToken);
    }

    private async Task RunAsync(Guid? listingId, LoadMode mode, CancellationToken cancellationToken)
    {
        CancellationTokenSource source;
        CancellationTokenSource? previous;
        ListingDto? listing;
        long generation;
        int page;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (mode != LoadMode.Detail && (_state.Listing is null || _state.IsComparing)) return;
            if (mode == LoadMode.MoreComparisons && !_state.HasMoreComparisons) return;
            listing = mode == LoadMode.Detail ? null : _state.Listing;
            page = mode == LoadMode.MoreComparisons ? _state.ComparisonPage + 1 : 1;
            _retryMore = mode == LoadMode.MoreComparisons;
            generation = ++_generation;
            previous = _pending;
            source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pending = source;
            _state = mode == LoadMode.Detail ? new(listingId, IsLoading: true)
                : _state with { IsComparing = true, ComparisonMessage = null };
        }
        TryCancel(previous);
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            if (mode == LoadMode.Detail)
            {
                listing = await marketplace.GetListingAsync(listingId!.Value, source.Token);
                source.Token.ThrowIfCancellationRequested();
                if (listing is null)
                {
                    Complete(generation, state => state with { IsLoading = false, NotFound = true, Message = "Este anúncio não está mais disponível." });
                    return;
                }
                string? metadataMessage = null;
                if (listing.Printing is null)
                {
                    try
                    {
                        var printing = await catalog.GetPrintingAsync(listing.PrintingId, source.Token);
                        var variant = printing.Variants.FirstOrDefault(item => item.Id == listing.VariantId)?.Code;
                        listing = listing with { Printing = new(printing.CardName, printing.SetName, printing.CollectorNumber,
                            printing.Language, printing.GameCode, printing.ArtworkUrl, variant) };
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !source.IsCancellationRequested)
                    {
                        metadataMessage = "Dados da carta indisponíveis. Confira as fotos e a descrição do vendedor.";
                    }
                }
                source.Token.ThrowIfCancellationRequested();
                var loaded = listing;
                if (!Complete(generation, state => state with { Listing = loaded, IsLoading = false, IsComparing = true, MetadataMessage = metadataMessage })) return;
            }
            try
            {
                var result = await marketplace.BrowseListingsAsync(new(null, listing!.PrintingId, listing.VariantId, null, null,
                    page, 20, "price_asc"), source.Token);
                source.Token.ThrowIfCancellationRequested();
                var compatible = result.Items.Where(item => item.PrintingId == listing.PrintingId && item.VariantId == listing.VariantId && item.Status == "active");
                Complete(generation, state => state with
                {
                    ComparisonItems = (mode == LoadMode.MoreComparisons ? state.Comparisons.Concat(compatible) : compatible).DistinctBy(item => item.Id).ToArray(),
                    IsComparing = false, ComparisonMessage = null, ComparisonPage = page,
                    HasMoreComparisons = result.Items.Count > 0 && page * Math.Max(1, result.PageSize) < result.TotalCount
                });
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !source.IsCancellationRequested)
            {
                Complete(generation, state => state with { IsComparing = false, ComparisonMessage = exception is OperationCanceledException
                    ? "A comparação demorou mais que o esperado. Tente novamente." : ApiErrorTranslator.FromException(exception).Message });
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            Complete(generation, state => state with { IsLoading = false, IsComparing = false });
        }
        catch (Exception exception)
        {
            Complete(generation, state => state with { IsLoading = false, IsComparing = false, Message = ApiErrorTranslator.FromException(exception).Message });
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_pending, source)) _pending = null; }
            source.Dispose();
        }
    }

    private bool Complete(long generation, Func<MarketplaceListingDetailState, MarketplaceListingDetailState> update)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation) return false;
            _state = update(_state);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void CancelPending()
    {
        CancellationTokenSource? previous;
        lock (_gate)
        {
            ++_generation;
            previous = _pending; _pending = null;
            _state = _state with { IsLoading = false, IsComparing = false };
        }
        TryCancel(previous);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private static void TryCancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); } catch (ObjectDisposedException) { }
    }
    public void Dispose() { lock (_gate) _disposed = true; CancelPending(); }
}
