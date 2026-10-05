using Vaulta.App.Core.Marketplace;
using Vaulta.App.ViewModels;
using Vaulta.App.Views.Components;
namespace Vaulta.App.Views;

public sealed class MarketplaceProductDetailPage : ContentPage,IQueryAttributable
{
    private readonly MarketplaceProductDetailViewModel _vm;
    private readonly CollectionView _offers;
    private bool _opening;
    public MarketplaceProductDetailPage(MarketplaceProductDetailViewModel vm)
    {
        _vm=vm;BindingContext=vm;Title="Mercado da carta";BackgroundColor=MarketplaceTheme.Background;
        Shell.SetNavBarIsVisible(this,false);Shell.SetTabBarIsVisible(this,false);
        _offers=new CollectionView{Margin=new Thickness(16,0),BackgroundColor=MarketplaceTheme.Background,
            SelectionMode=SelectionMode.Single,ItemSizingStrategy=ItemSizingStrategy.MeasureAllItems,
            ItemTemplate=new DataTemplate(()=>new MarketplaceOfferRowView()),Header=Header(),Footer=Footer(),RemainingItemsThreshold=3};
        _offers.SetBinding(ItemsView.ItemsSourceProperty,nameof(vm.Offers));
        _offers.SetBinding(ItemsView.RemainingItemsThresholdReachedCommandProperty,nameof(vm.LoadMoreCommand));
        _offers.SelectionChanged+=OpenOffer;Content=_offers;
    }
    public void ApplyQueryAttributes(IDictionary<string,object> query)
    {
        if(!query.TryGetValue("printingId",out var p)||!Guid.TryParse(p.ToString(),out var id))return;
        if(!query.TryGetValue("variantKey",out var key))return;
        var text=key.ToString();
        if(text=="none")_vm.Configure(id,null);
        else if(Guid.TryParse(text,out var variant))_vm.Configure(id,variant);
    }
    protected override async void OnAppearing(){base.OnAppearing();await _vm.ActivateAsync();}
    protected override void OnDisappearing(){_vm.Deactivate();base.OnDisappearing();}
    private View Header()
    {
        var layout=new VerticalStackLayout{Spacing=12,Padding=new Thickness(0,12,0,16)};
        var back=MarketplaceTheme.Action("‹ Voltar");back.Clicked+=async(_,_)=>await Shell.Current.GoToAsync("..");
        layout.Children.Add(back);
        var title=MarketplaceTheme.Text("Mercado da carta",20,true);SemanticProperties.SetHeadingLevel(title,SemanticHeadingLevel.Level1);layout.Children.Add(title);
        var loading=new ActivityIndicator{Color=MarketplaceTheme.Brand};loading.SetBinding(ActivityIndicator.IsRunningProperty,nameof(_vm.IsLoading));loading.SetBinding(IsVisibleProperty,nameof(_vm.IsLoading));layout.Children.Add(loading);
        var failure=new VerticalStackLayout{Spacing=8};var message=MarketplaceTheme.Text(null,14,secondary:true);message.SetBinding(Label.TextProperty,nameof(_vm.StatusMessage));failure.Children.Add(message);
        var retry=MarketplaceTheme.Action("Tentar novamente");retry.SetBinding(Button.CommandProperty,nameof(_vm.RefreshCommand));failure.Children.Add(retry);failure.SetBinding(IsVisibleProperty,nameof(_vm.HasProblem));layout.Children.Add(failure);
        var info=new VerticalStackLayout{Spacing=12};info.SetBinding(IsVisibleProperty,nameof(_vm.HasProduct));
        Label Field(string property,double size,bool bold=false,bool secondary=false)
        {var l=MarketplaceTheme.Text(null,size,bold,secondary);l.LineBreakMode=LineBreakMode.WordWrap;l.SetBinding(Label.TextProperty,"Card."+property);return l;}
        var artwork=new Image{Aspect=Aspect.AspectFit,HeightRequest=230};artwork.SetBinding(Image.SourceProperty,"Card.ArtworkUrl");SemanticProperties.SetDescription(artwork,"Imagem de referência do catálogo");
        var missing=MarketplaceTheme.Text("Imagem de referência indisponível",12,secondary:true);
        missing.HorizontalTextAlignment=TextAlignment.Center;missing.VerticalTextAlignment=TextAlignment.Center;
        missing.SetBinding(IsVisibleProperty,nameof(_vm.ShowArtworkPlaceholder));
        info.Children.Add(new Grid{HeightRequest=230,Children={artwork,missing}});
        info.Children.Add(Field(nameof(MarketplaceProductPresentation.Name),24,true));
        info.Children.Add(Field(nameof(MarketplaceProductPresentation.Identity),14,secondary:true));
        info.Children.Add(Field(nameof(MarketplaceProductPresentation.Finish),12,secondary:true));
        var market=new VerticalStackLayout{Spacing=8,Padding=16,BackgroundColor=MarketplaceTheme.Surface};
        market.Children.Add(MarketplaceTheme.Text("REFERÊNCIA DE MERCADO",12,secondary:true));
        market.Children.Add(Field(nameof(MarketplaceProductPresentation.MarketPrice),24,true));
        market.Children.Add(Field(nameof(MarketplaceProductPresentation.MarketSource),12,secondary:true));
        market.Children.Add(MarketplaceTheme.Text("A partir de",12,secondary:true));
        market.Children.Add(Field(nameof(MarketplaceProductPresentation.LowestPrice),18,true));
        market.Children.Add(Field(nameof(MarketplaceProductPresentation.OfferCount),14));
        info.Children.Add(market);
        info.Children.Add(MarketplaceTheme.Text("Histórico de mercado ainda indisponível",12,secondary:true));
        var offers=MarketplaceTheme.Text("Ofertas desta impressão",24,true);SemanticProperties.SetHeadingLevel(offers,SemanticHeadingLevel.Level2);info.Children.Add(offers);
        info.Children.Add(MarketplaceTheme.Text("Confira condição e fotos de cada exemplar.",12,secondary:true));layout.Children.Add(info);
        return layout;
    }
    private View Footer()
    {
        var footer=new VerticalStackLayout{Spacing=12,Padding=new Thickness(0,8,0,24)};
        var busy=new ActivityIndicator{Color=MarketplaceTheme.Brand};busy.SetBinding(ActivityIndicator.IsRunningProperty,nameof(_vm.IsLoadingOffers));busy.SetBinding(IsVisibleProperty,nameof(_vm.IsLoadingOffers));footer.Children.Add(busy);
        var empty=MarketplaceTheme.Text("Sem ofertas disponíveis. A cotação é uma referência de mercado.",14,secondary:true);empty.SetBinding(IsVisibleProperty,nameof(_vm.OffersEmpty));footer.Children.Add(empty);
        var problem=new VerticalStackLayout{Spacing=8};var message=MarketplaceTheme.Text(null,14,secondary:true);message.SetBinding(Label.TextProperty,nameof(_vm.OffersMessage));problem.Children.Add(message);
        var retry=MarketplaceTheme.Action("Tentar carregar ofertas novamente");retry.SetBinding(Button.CommandProperty,nameof(_vm.RetryOffersCommand));problem.Children.Add(retry);problem.SetBinding(IsVisibleProperty,nameof(_vm.HasOffersProblem));footer.Children.Add(problem);
        var more=MarketplaceTheme.Action("Ver mais ofertas");more.SetBinding(Button.CommandProperty,nameof(_vm.LoadMoreCommand));more.SetBinding(IsVisibleProperty,nameof(_vm.HasMore));footer.Children.Add(more);return footer;
    }
    private async void OpenOffer(object? sender,SelectionChangedEventArgs args)
    {
        var listing=args.CurrentSelection.OfType<MarketplaceListingPresentation>().FirstOrDefault();_offers.SelectedItem=null;
        if(listing is null||_opening)return;_opening=true;
        try{await Shell.Current.GoToAsync($"marketplace-listing?listingId={listing.Id}");}finally{_opening=false;}
    }
}
