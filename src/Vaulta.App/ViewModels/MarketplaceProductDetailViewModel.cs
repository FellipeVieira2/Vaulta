using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vaulta.App.Core.Marketplace;
using Vaulta.Marketplace.Contracts;
namespace Vaulta.App.ViewModels;

public partial class MarketplaceProductDetailViewModel(MarketplaceProductDetailController controller) : ObservableObject
{
    private bool _active;
    private Guid? _printingId, _variantId;
    public ObservableCollection<MarketplaceListingPresentation> Offers {get;}=[];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowArtworkPlaceholder))] private MarketplaceProductPresentation? card;
    public bool ShowArtworkPlaceholder => string.IsNullOrWhiteSpace(Card?.ArtworkUrl);
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isLoadingOffers;
    [ObservableProperty] private bool hasProduct;
    [ObservableProperty] private bool hasProblem;
    [ObservableProperty] private bool hasOffersProblem;
    [ObservableProperty] private bool offersEmpty;
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private string? offersMessage;
    public void Configure(Guid printingId,Guid? variantId){_printingId=printingId;_variantId=variantId;}
    public async Task ActivateAsync(){if(!_active){_active=true;controller.StateChanged+=Changed;}await RefreshAsync();}
    public void Deactivate(){_active=false;controller.StateChanged-=Changed;controller.CancelPending();}
    [RelayCommand(AllowConcurrentExecutions=true)]
    private Task RefreshAsync()=>_printingId is {} id?controller.LoadAsync(id,_variantId):Task.CompletedTask;
    [RelayCommand] private Task RetryOffersAsync()=>controller.RetryOffersAsync();
    [RelayCommand] private Task LoadMoreAsync()=>controller.LoadMoreOffersAsync();
    private void Changed(object? sender,EventArgs args)=>MainThread.BeginInvokeOnMainThread(()=>
    {
        if(!_active)return;
        var s=controller.State;
        IsLoading=s.IsLoading;IsLoadingOffers=s.IsLoadingOffers;
        Card=s.Product is {} product?MarketplaceProductPresentation.From(product):null;
        HasProduct=s.Product is not null;HasProblem=s.Message is not null;StatusMessage=s.Message;
        HasOffersProblem=s.OffersMessage is not null;OffersMessage=s.OffersMessage;
        HasMore=s.HasMore&&!s.IsLoadingOffers&&!HasOffersProblem;
        OffersEmpty=HasProduct&&!s.IsLoadingOffers&&!HasOffersProblem&&s.Offers.Count==0;
        var mapped=s.Offers.Select(x=>MarketplaceListingPresentation.From(x,DateTimeOffset.UtcNow)).ToArray();
        for(var i=0;i<mapped.Length;i++){if(i>=Offers.Count)Offers.Add(mapped[i]);else if(Offers[i]!=mapped[i])Offers[i]=mapped[i];}
        while(Offers.Count>mapped.Length)Offers.RemoveAt(Offers.Count-1);
    });
}
