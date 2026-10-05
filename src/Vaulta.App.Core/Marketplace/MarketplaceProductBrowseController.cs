using Vaulta.App.Core.Http;
using Vaulta.Marketplace.Contracts;

namespace Vaulta.App.Core.Marketplace;

public sealed record MarketplaceProductFilters(string? Query = null, string? GameCode = "pokemon", string Sort = "newest",
    Guid? SetId = null, string? Language = null, string? VariantCode = null, string? Condition = null,
    decimal? MinPriceBrl = null, decimal? MaxPriceBrl = null, bool PhotosOnly = false)
{
    internal MarketplaceProductFilters Normalize() => this with
    {
        Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim(),
        GameCode = string.IsNullOrWhiteSpace(GameCode) ? null : GameCode.Trim().ToLowerInvariant(),
        Sort = Sort is "price_asc" or "price_desc" ? Sort : "newest"
    };
    internal BrowseProductsQueryDto ToQuery(int page) => new(Query, GameCode, page, 20, Sort, SetId, Language,
        VariantCode, Condition, MinPriceBrl, MaxPriceBrl, PhotosOnly);
}


public sealed record MarketplaceProductBrowseState(
    IReadOnlyList<MarketplaceProductDto> Items,
    MarketplaceProductFilters Filters,
    int Page = 0,
    int TotalCount = 0,
    bool HasLoaded = false,
    bool IsLoading = false,
    bool IsLoadingMore = false,
    bool HasMore = false,
    MarketplaceHomeProblem Problem = MarketplaceHomeProblem.None,
    string? Message = null)
{
    public bool IsBusy => IsLoading || IsLoadingMore;
    public bool IsEmpty => HasLoaded && !IsBusy && Problem == MarketplaceHomeProblem.None && Items.Count == 0;
}

/// <summary>Owns paged marketplace state independently of MAUI and rejects obsolete responses.</summary>
public sealed class MarketplaceProductBrowseController(IMarketplaceProductClient client, Func<bool>? isConnected = null) : IDisposable
{
    private readonly object _gate = new();
    private MarketplaceProductBrowseState _state = new([], new());
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _disposed;
    private bool _retryMore;

    public MarketplaceProductBrowseState State { get { lock (_gate) return _state; } }
    public event EventHandler? StateChanged;

    public Task RefreshAsync(MarketplaceProductFilters? filters = null, CancellationToken cancellationToken = default) =>
        RequestAsync(filters, append: false, cancellationToken);

    public Task LoadMoreAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(null, append: true, cancellationToken);

    public Task RetryAsync(CancellationToken cancellationToken = default)
    {
        bool append;
        lock (_gate) append = _retryMore;
        return RequestAsync(null, append, cancellationToken);
    }

    public void CancelPending()
    {
        CancellationTokenSource? previous;
        lock (_gate)
        {
            ++_generation;
            previous = _pending;
            _pending = null;
            _state = _state with { IsLoading = false, IsLoadingMore = false };
        }
        TryCancel(previous);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RequestAsync(MarketplaceProductFilters? filters, bool append, CancellationToken cancellationToken)
    {
        CancellationTokenSource source;
        CancellationTokenSource? previous;
        MarketplaceProductFilters selected;
        long generation;
        int page;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (append && (_state.IsBusy || !_state.HasMore)) return;
            selected = (filters ?? _state.Filters).Normalize();
            page = append ? _state.Page + 1 : 1;
            generation = ++_generation;
            previous = _pending;
            source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pending = source;
            _retryMore = append;
            var sameFilters = selected == _state.Filters;
            _state = _state with
            {
                Filters = selected,
                Items = append || sameFilters ? _state.Items : [],
                Page = append ? _state.Page : 0,
                HasLoaded = append && _state.HasLoaded,
                IsLoading = !append,
                IsLoadingMore = append,
                HasMore = append && _state.HasMore,
                Problem = MarketplaceHomeProblem.None,
                Message = null
            };
        }
        TryCancel(previous);
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            if (isConnected?.Invoke() == false)
            {
                Complete(generation, state => state with
                {
                    IsLoading = false, IsLoadingMore = false,
                    Problem = MarketplaceHomeProblem.Offline,
                    Message = "Você está sem conexão. Verifique sua rede e tente novamente."
                });
                return;
            }
            var result = await client.BrowseProductsAsync(selected.ToQuery(page), source.Token);
            source.Token.ThrowIfCancellationRequested();
            Complete(generation, state =>
            {
                var items = (append ? state.Items.Concat(result.Items) : result.Items).DistinctBy(item => (item.PrintingId, item.VariantId)).ToArray();
                return state with
                {
                    Items = items, Page = page, TotalCount = result.TotalCount, HasLoaded = true,
                    IsLoading = false, IsLoadingMore = false,
                    HasMore = result.Items.Count > 0 && items.Length < result.TotalCount && (!append || items.Length > state.Items.Count),
                    Problem = MarketplaceHomeProblem.None, Message = null
                };
            });
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            Complete(generation, state => state with { IsLoading = false, IsLoadingMore = false });
        }
        catch (Exception exception)
        {
            var offline = isConnected?.Invoke() == false;
            Complete(generation, state => state with
            {
                IsLoading = false, IsLoadingMore = false,
                Problem = offline ? MarketplaceHomeProblem.Offline : MarketplaceHomeProblem.Error,
                Message = ApiErrorTranslator.FromException(exception).Message
            });
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_pending, source)) _pending = null; }
            source.Dispose();
        }
    }

    private void Complete(long generation, Func<MarketplaceProductBrowseState, MarketplaceProductBrowseState> update)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation) return;
            _state = update(_state);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        CancelPending();
    }
}
