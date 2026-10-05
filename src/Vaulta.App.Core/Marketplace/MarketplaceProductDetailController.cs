using Vaulta.App.Core.Http;
using Vaulta.Marketplace.Contracts;
namespace Vaulta.App.Core.Marketplace;

public sealed record MarketplaceProductDetailState(Guid? PrintingId = null, Guid? VariantId = null, MarketplaceProductDto? Product = null,
    IReadOnlyList<ListingDto>? OfferItems = null, bool IsLoading = false, bool IsLoadingOffers = false, bool NotFound = false,
    string? Message = null, string? OffersMessage = null, int Page = 0, bool HasMore = false)
{
    public IReadOnlyList<ListingDto> Offers => OfferItems ?? [];
}

public sealed class MarketplaceProductDetailController(IMarketplaceProductClient client) : IDisposable
{
    private readonly object _gate = new();
    private MarketplaceProductDetailState _state = new();
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _retryMore, _disposed;
    public MarketplaceProductDetailState State { get { lock(_gate) return _state; } }
    public event EventHandler? StateChanged;
    public Task LoadAsync(Guid printingId, Guid? variantId, CancellationToken ct = default) => Request(printingId,variantId,true,false,ct);
    public Task LoadMoreOffersAsync(CancellationToken ct = default) => Request(null,null,false,true,ct);
    public Task RetryOffersAsync(CancellationToken ct = default) { bool more; lock(_gate) more=_retryMore; return Request(null,null,false,more,ct); }

    private async Task Request(Guid? printingId, Guid? variantId, bool loadProduct, bool more, CancellationToken ct)
    {
        CancellationTokenSource source; CancellationTokenSource? previous; long generation; int page; Guid identity; Guid? finish;
        lock(_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if(!loadProduct && (_state.Product is null || _state.IsLoadingOffers || (more && !_state.HasMore))) return;
            identity = loadProduct ? printingId!.Value : _state.PrintingId!.Value;
            finish = loadProduct ? variantId : _state.VariantId;
            page = more ? _state.Page+1 : 1; _retryMore=more; generation=++_generation;
            previous=_pending; source=CancellationTokenSource.CreateLinkedTokenSource(ct); _pending=source;
            _state = loadProduct ? new(identity,finish,IsLoading:true) : _state with { IsLoadingOffers=true,OffersMessage=null };
        }
        Cancel(previous); StateChanged?.Invoke(this,EventArgs.Empty);
        try
        {
            if(loadProduct)
            {
                var product = await client.GetProductAsync(identity,finish,source.Token); source.Token.ThrowIfCancellationRequested();
                if(product is null) { Complete(generation,s=>s with {IsLoading=false,NotFound=true,Message="Esta impressão não está disponível."}); return; }
                if(!Complete(generation,s=>s with {Product=product,IsLoading=false,IsLoadingOffers=true})) return;
            }
            try
            {
                var result=await client.GetOffersAsync(identity,finish,page,20,source.Token); source.Token.ThrowIfCancellationRequested();
                var valid=result.Items.Where(x=>x.PrintingId==identity && x.VariantId==finish && x.Status=="active");
                Complete(generation,s=>
                {
                    var items=(more?s.Offers.Concat(valid):valid).DistinctBy(x=>x.Id).ToArray();
                    return s with {OfferItems=items,IsLoadingOffers=false,OffersMessage=null,Page=page,
                        HasMore=result.Items.Count>0 && items.Length<result.TotalCount && (!more || items.Length>s.Offers.Count)};
                });
            }
            catch(Exception e) when(e is not OperationCanceledException || !source.IsCancellationRequested)
            {Complete(generation,s=>s with {IsLoadingOffers=false,OffersMessage=ApiErrorTranslator.FromException(e).Message});}
        }
        catch(OperationCanceledException) when(source.IsCancellationRequested)
        {Complete(generation,s=>s with {IsLoading=false,IsLoadingOffers=false});}
        catch(Exception e){Complete(generation,s=>s with {IsLoading=false,IsLoadingOffers=false,Message=ApiErrorTranslator.FromException(e).Message});}
        finally{lock(_gate){if(ReferenceEquals(_pending,source))_pending=null;}source.Dispose();}
    }
    private bool Complete(long generation,Func<MarketplaceProductDetailState,MarketplaceProductDetailState> update)
    {lock(_gate){if(_disposed || generation!=_generation)return false;_state=update(_state);}StateChanged?.Invoke(this,EventArgs.Empty);return true;}
    public void CancelPending()
    {
        CancellationTokenSource? previous;
        lock(_gate){++_generation;previous=_pending;_pending=null;_state=_state with{IsLoading=false,IsLoadingOffers=false};}
        Cancel(previous);StateChanged?.Invoke(this,EventArgs.Empty);
    }
    private static void Cancel(CancellationTokenSource? source){try{source?.Cancel();}catch(ObjectDisposedException){}}
    public void Dispose(){lock(_gate)_disposed=true;CancelPending();}
}
