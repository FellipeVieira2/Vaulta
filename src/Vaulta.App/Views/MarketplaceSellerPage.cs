using Vaulta.App.Core.Marketplace;
using Vaulta.App.ViewModels;
using Vaulta.App.Views.Components;

namespace Vaulta.App.Views;

public sealed class MarketplaceSellerPage : ContentPage, IQueryAttributable
{
    private readonly MarketplaceSellerViewModel _viewModel;
    private readonly CollectionView _list;
    private bool _navigating;
    public MarketplaceSellerPage(MarketplaceSellerViewModel viewModel)
    {
        _viewModel = viewModel; BindingContext = viewModel;
        Title = "Vendedor"; BackgroundColor = MarketplaceTheme.Background;
        Shell.SetNavBarIsVisible(this, false); Shell.SetTabBarIsVisible(this, false);
        _list = new CollectionView
        {
            BackgroundColor = MarketplaceTheme.Background, Margin = new Thickness(16, 0),
            ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 16 },
            SelectionMode = SelectionMode.Single, ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems,
            RemainingItemsThreshold = 4, ItemTemplate = new DataTemplate(() => new MarketplaceListingCardView()),
            Header = BuildHeader(), Footer = BuildFooter()
        };
        _list.SetBinding(ItemsView.ItemsSourceProperty, nameof(MarketplaceSellerViewModel.Listings));
        _list.SetBinding(ItemsView.RemainingItemsThresholdReachedCommandProperty, nameof(MarketplaceSellerViewModel.LoadMoreCommand));
        _list.SelectionChanged += OpenListing; Content = _list;
    }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("sellerUserId", out var value) && Guid.TryParse(value.ToString(), out var id)) _viewModel.Configure(id);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await _viewModel.ActivateAsync(); }
    protected override void OnDisappearing() { _viewModel.Deactivate(); base.OnDisappearing(); }
    private View BuildHeader()
    {
        var header = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 12, 0, 16) };
        var back = new ImageButton { Source = "icon_arrow_left.svg", WidthRequest = 48, HeightRequest = 48, Padding = 12, BackgroundColor = Colors.Transparent };
        SemanticProperties.SetDescription(back, "Voltar ao anúncio"); back.Clicked += async (_, _) => await Shell.Current.GoToAsync("..");
        var top = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = 48 }, new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = 48 } } };
        var title = MarketplaceTheme.Text("Vendedor", 13, bold: true); title.HorizontalTextAlignment = TextAlignment.Center; title.VerticalOptions = LayoutOptions.Center;
        top.Add(back); top.Add(title, 1); header.Children.Add(top);
        var name = MarketplaceTheme.Text(null, 20, bold: true); name.SetBinding(Label.TextProperty, nameof(MarketplaceSellerViewModel.Seller));
        SemanticProperties.SetHeadingLevel(name, SemanticHeadingLevel.Level1); header.Children.Add(name);
        header.Children.Add(MarketplaceTheme.Text("Dados públicos do perfil indisponíveis", 11, secondary: true));
        var rating = MarketplaceTheme.Text(null, 11, secondary: true); rating.SetBinding(Label.TextProperty, nameof(MarketplaceSellerViewModel.Rating)); header.Children.Add(rating);
        var count = MarketplaceTheme.Text(null, 13); count.SetBinding(Label.TextProperty, nameof(MarketplaceSellerViewModel.Count)); header.Children.Add(count);
        var section = MarketplaceTheme.Text("Anúncios do vendedor", 16, bold: true); SemanticProperties.SetHeadingLevel(section, SemanticHeadingLevel.Level2); header.Children.Add(section);
        var activity = new ActivityIndicator { Color = MarketplaceTheme.Brand, HeightRequest = 48 };
        activity.SetBinding(ActivityIndicator.IsRunningProperty, nameof(MarketplaceSellerViewModel.IsLoading));
        activity.SetBinding(IsVisibleProperty, nameof(MarketplaceSellerViewModel.IsLoading)); header.Children.Add(activity);
        header.Children.Add(Problem(nameof(MarketplaceSellerViewModel.ShowInitialProblem)));
        var empty = MarketplaceTheme.Text("Este vendedor não tem anúncios ativos disponíveis.", 13, secondary: true);
        empty.SetBinding(IsVisibleProperty, nameof(MarketplaceSellerViewModel.IsEmpty)); header.Children.Add(empty);
        return header;
    }
    private View BuildFooter()
    {
        var footer = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 8, 0, 24) };
        footer.Children.Add(Problem(nameof(MarketplaceSellerViewModel.ShowFooterProblem)));
        var activity = new ActivityIndicator { Color = MarketplaceTheme.Brand, HeightRequest = 36 };
        activity.SetBinding(ActivityIndicator.IsRunningProperty, nameof(MarketplaceSellerViewModel.IsLoadingMore));
        activity.SetBinding(IsVisibleProperty, nameof(MarketplaceSellerViewModel.IsLoadingMore)); footer.Children.Add(activity);
        var more = MarketplaceTheme.Action("Carregar mais anúncios");
        more.SetBinding(Button.CommandProperty, nameof(MarketplaceSellerViewModel.LoadMoreCommand));
        more.SetBinding(IsVisibleProperty, nameof(MarketplaceSellerViewModel.HasMore)); footer.Children.Add(more);
        var refresh = MarketplaceTheme.Action("Atualizar anúncios"); refresh.SetBinding(Button.CommandProperty, nameof(MarketplaceSellerViewModel.RefreshCommand)); footer.Children.Add(refresh);
        return footer;
    }
    private static View Problem(string visibility)
    {
        var problem = new VerticalStackLayout { Spacing = 8 };
        var message = MarketplaceTheme.Text(null, 13, secondary: true); message.SetBinding(Label.TextProperty, nameof(MarketplaceSellerViewModel.StatusMessage));
        var retry = MarketplaceTheme.Action("Tentar novamente"); retry.SetBinding(Button.CommandProperty, nameof(MarketplaceSellerViewModel.RetryCommand));
        problem.Children.Add(message); problem.Children.Add(retry); problem.SetBinding(IsVisibleProperty, visibility); return problem;
    }
    private async void OpenListing(object? sender, SelectionChangedEventArgs args)
    {
        var listing = args.CurrentSelection.OfType<MarketplaceListingPresentation>().FirstOrDefault(); _list.SelectedItem = null;
        if (listing is null || _navigating) return;
        _navigating = true;
        try { await Shell.Current.GoToAsync($"marketplace-listing?listingId={listing.Id}"); } finally { _navigating = false; }
    }
}
