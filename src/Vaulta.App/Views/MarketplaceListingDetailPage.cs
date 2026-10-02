using Microsoft.Maui.Controls.Shapes;
using Vaulta.App.Core.Marketplace;
using Vaulta.App.State;
using Vaulta.App.ViewModels;
using Vaulta.App.Views.Components;

namespace Vaulta.App.Views;

public sealed class MarketplaceListingDetailPage : ContentPage, IQueryAttributable
{
    private readonly MarketplaceListingDetailViewModel _viewModel;
    private readonly SessionState _session;
    private readonly CollectionView _comparisons;
    private bool _navigating;

    public MarketplaceListingDetailPage(MarketplaceListingDetailViewModel viewModel, SessionState session)
    {
        _viewModel = viewModel; _session = session; BindingContext = viewModel;
        Title = "Anúncio"; BackgroundColor = MarketplaceTheme.Background;
        Shell.SetNavBarIsVisible(this, false); Shell.SetTabBarIsVisible(this, false);
        _comparisons = new CollectionView
        {
            BackgroundColor = MarketplaceTheme.Background, Margin = new Thickness(16, 0),
            SelectionMode = SelectionMode.Single, ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems,
            ItemTemplate = new DataTemplate(() => new MarketplaceComparisonRowView()),
            Header = BuildHeader(), Footer = BuildFooter()
        };
        _comparisons.SetBinding(ItemsView.ItemsSourceProperty, nameof(MarketplaceListingDetailViewModel.Comparisons));
        _comparisons.SelectionChanged += OpenComparison;
        var buy = MarketplaceTheme.Action("Comprar agora");
        buy.BackgroundColor = MarketplaceTheme.Brand; buy.TextColor = MarketplaceTheme.Background;
        buy.SetBinding(Button.TextProperty, nameof(MarketplaceListingDetailViewModel.BuyLabel));
        buy.SetBinding(IsEnabledProperty, nameof(MarketplaceListingDetailViewModel.CanBuy));
        buy.Clicked += Buy;
        var fixedAction = new Border
        {
            BackgroundColor = MarketplaceTheme.Surface, StrokeThickness = 0,
            Padding = new Thickness(16, 8, 16, 20), Content = buy
        };
        var layout = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Star }, new RowDefinition { Height = GridLength.Auto } } };
        layout.Add(_comparisons); layout.Add(fixedAction, 0, 1); Content = layout;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("listingId", out var value) && Guid.TryParse(value.ToString(), out var id)) _viewModel.Configure(id);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await _viewModel.ActivateAsync(); }
    protected override void OnDisappearing() { _viewModel.Deactivate(); base.OnDisappearing(); }

    private View BuildHeader()
    {
        var header = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 12, 0, 8) };
        header.Children.Add(BuildTopBar());
        var loading = new ActivityIndicator { Color = MarketplaceTheme.Brand, HeightRequest = 48 };
        loading.SetBinding(ActivityIndicator.IsRunningProperty, nameof(MarketplaceListingDetailViewModel.IsLoading));
        loading.SetBinding(IsVisibleProperty, nameof(MarketplaceListingDetailViewModel.IsLoading)); header.Children.Add(loading);
        var problem = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 24) };
        var message = MarketplaceTheme.Text(null, 13, secondary: true);
        message.SetBinding(Label.TextProperty, nameof(MarketplaceListingDetailViewModel.StatusMessage));
        var retry = MarketplaceTheme.Action("Atualizar anúncio");
        retry.SetBinding(Button.CommandProperty, nameof(MarketplaceListingDetailViewModel.RefreshCommand));
        problem.Children.Add(message); problem.Children.Add(retry);
        problem.SetBinding(IsVisibleProperty, nameof(MarketplaceListingDetailViewModel.HasProblem)); header.Children.Add(problem);

        var info = new VerticalStackLayout { Spacing = 4 };
        info.SetBinding(IsVisibleProperty, nameof(MarketplaceListingDetailViewModel.HasListing));
        var image = new Image { HeightRequest = 226, Aspect = Aspect.AspectFit };
        image.SetBinding(Image.SourceProperty, nameof(MarketplaceListingDetailViewModel.ImageUrl));
        image.SetBinding(SemanticProperties.DescriptionProperty, nameof(MarketplaceListingDetailViewModel.ImageDescription));
        var missing = MarketplaceTheme.Text("Sem imagem disponível", 13, secondary: true);
        missing.HorizontalTextAlignment = TextAlignment.Center; missing.VerticalTextAlignment = TextAlignment.Center;
        missing.SetBinding(IsVisibleProperty, nameof(MarketplaceListingDetailViewModel.ShowImagePlaceholder));
        info.Children.Add(new Border { HeightRequest = 226, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 8 }, Content = new Grid { Children = { image, missing } } });
        var caption = MarketplaceTheme.Text(null, 11, secondary: true);
        caption.SetBinding(Label.TextProperty, nameof(MarketplaceListingDetailViewModel.ImageDescription)); info.Children.Add(caption);
        var photos = new CollectionView
        {
            HeightRequest = 96, ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal) { ItemSpacing = 8 },
            SelectionMode = SelectionMode.None, BackgroundColor = MarketplaceTheme.Background,
            ItemTemplate = new DataTemplate(() =>
            {
                var thumbnail = new ImageButton { WidthRequest = 64, HeightRequest = 64, Padding = 0, Aspect = Aspect.AspectFit, BackgroundColor = MarketplaceTheme.Surface };
                thumbnail.SetBinding(ImageButton.SourceProperty, nameof(MarketplaceListingPhoto.Url));
                thumbnail.Command = _viewModel.SelectPhotoCommand; thumbnail.SetBinding(ImageButton.CommandParameterProperty, ".");
                thumbnail.SetBinding(SemanticProperties.DescriptionProperty, nameof(MarketplaceListingPhoto.Description));
                var label = MarketplaceTheme.Text(null, 10, secondary: true); label.WidthRequest = 88; label.MaxLines = 2;
                label.SetBinding(Label.TextProperty, nameof(MarketplaceListingPhoto.Description));
                return new VerticalStackLayout { Spacing = 4, WidthRequest = 88, Children = { thumbnail, label } };
            })
        };
        photos.SetBinding(ItemsView.ItemsSourceProperty, nameof(MarketplaceListingDetailViewModel.Photos)); info.Children.Add(photos);
        var enlarge = MarketplaceTheme.Action("Ampliar foto");
        enlarge.SetBinding(IsEnabledProperty, nameof(MarketplaceListingDetailViewModel.HasImage));
        enlarge.Clicked += EnlargePhoto; info.Children.Add(enlarge);
        var notice = MarketplaceTheme.Text(null, 11, secondary: true);
        notice.SetBinding(Label.TextProperty, nameof(MarketplaceListingDetailViewModel.PhotoNotice)); info.Children.Add(notice);
        var name = MarketplaceTheme.Text(null, 20, bold: true);
        name.SetBinding(Label.TextProperty, $"{nameof(MarketplaceListingDetailViewModel.Card)}.{nameof(MarketplaceListingPresentation.Name)}");
        SemanticProperties.SetHeadingLevel(name, SemanticHeadingLevel.Level1); info.Children.Add(name);
        var printing = MarketplaceTheme.Text(null, 13, secondary: true);
        printing.SetBinding(Label.TextProperty, nameof(MarketplaceListingDetailViewModel.PrintingDetail)); info.Children.Add(printing);
        var condition = MarketplaceTheme.Text(null, 13);
        condition.SetBinding(Label.TextProperty, $"{nameof(MarketplaceListingDetailViewModel.Card)}.{nameof(MarketplaceListingPresentation.Metadata)}"); info.Children.Add(condition);
        var price = MarketplaceTheme.Text(null, 18, bold: true);
        price.SetBinding(Label.TextProperty, $"{nameof(MarketplaceListingDetailViewModel.Card)}.{nameof(MarketplaceListingPresentation.Price)}"); info.Children.Add(price);
        var metadata = MarketplaceTheme.Text(null, 11, secondary: true);
        metadata.SetBinding(Label.TextProperty, nameof(MarketplaceListingDetailViewModel.MetadataMessage));
        metadata.SetBinding(IsVisibleProperty, nameof(MarketplaceListingDetailViewModel.HasMetadataMessage)); info.Children.Add(metadata);
        info.Children.Add(MarketplaceTheme.Text("Referência de mercado indisponível", 11, secondary: true));
        var seller = MarketplaceTheme.Action(""); seller.HorizontalOptions = LayoutOptions.Start; seller.Padding = 0;
        seller.TextColor = MarketplaceTheme.Primary; seller.SetBinding(Button.TextProperty, nameof(MarketplaceListingDetailViewModel.Seller));
        seller.Clicked += OpenSeller; SemanticProperties.SetHint(seller, "Abrir anúncios e avaliações do vendedor"); info.Children.Add(seller);
        var rating = MarketplaceTheme.Text(null, 11, secondary: true);
        rating.SetBinding(Label.TextProperty, nameof(MarketplaceListingDetailViewModel.Rating)); info.Children.Add(rating);
        info.Children.Add(MarketplaceTheme.Text("Frete e prazo a confirmar antes da compra", 13, secondary: true));
        info.Children.Add(MarketplaceTheme.Text("Descrição do vendedor", 13, bold: true));
        var description = MarketplaceTheme.Text(null, 13, secondary: true);
        description.SetBinding(Label.TextProperty, nameof(MarketplaceListingDetailViewModel.Description)); info.Children.Add(description);
        var comparisonTitle = MarketplaceTheme.Text("Compare vendedores", 16, bold: true);
        comparisonTitle.Margin = new Thickness(0, 8, 0, 0); SemanticProperties.SetHeadingLevel(comparisonTitle, SemanticHeadingLevel.Level2); info.Children.Add(comparisonTitle);
        info.Children.Add(MarketplaceTheme.Text("Mesma impressão, variante e idioma", 11, secondary: true));
        var comparing = new ActivityIndicator { Color = MarketplaceTheme.Brand, HeightRequest = 36 };
        comparing.SetBinding(ActivityIndicator.IsRunningProperty, nameof(MarketplaceListingDetailViewModel.IsComparing));
        comparing.SetBinding(IsVisibleProperty, nameof(MarketplaceListingDetailViewModel.IsComparing)); info.Children.Add(comparing);
        var empty = MarketplaceTheme.Text("Não há anúncios compatíveis disponíveis para comparar.", 11, secondary: true);
        empty.SetBinding(IsVisibleProperty, nameof(MarketplaceListingDetailViewModel.ComparisonEmpty)); info.Children.Add(empty);
        header.Children.Add(info);
        return header;
    }

    private View BuildFooter()
    {
        var footer = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 8, 0, 24) };
        var problem = new VerticalStackLayout { Spacing = 8 };
        var message = MarketplaceTheme.Text(null, 11, secondary: true);
        message.SetBinding(Label.TextProperty, nameof(MarketplaceListingDetailViewModel.ComparisonMessage));
        var retry = MarketplaceTheme.Action("Tentar comparação novamente");
        retry.SetBinding(Button.CommandProperty, nameof(MarketplaceListingDetailViewModel.RetryComparisonsCommand));
        problem.Children.Add(message); problem.Children.Add(retry);
        problem.SetBinding(IsVisibleProperty, nameof(MarketplaceListingDetailViewModel.HasComparisonProblem)); footer.Children.Add(problem);
        var more = MarketplaceTheme.Action("Comparar mais anúncios");
        more.SetBinding(Button.CommandProperty, nameof(MarketplaceListingDetailViewModel.LoadMoreComparisonsCommand));
        more.SetBinding(IsVisibleProperty, nameof(MarketplaceListingDetailViewModel.HasMoreComparisons)); footer.Children.Add(more);
        var history = MarketplaceTheme.Text("Histórico de preços indisponível", 11, secondary: true);
        history.SetBinding(IsVisibleProperty, nameof(MarketplaceListingDetailViewModel.HasListing)); footer.Children.Add(history);
        return footer;
    }

    private static View BuildTopBar()
    {
        var back = new ImageButton { Source = "icon_arrow_left.png", WidthRequest = 48, HeightRequest = 48, Padding = 12, BackgroundColor = Colors.Transparent };
        SemanticProperties.SetDescription(back, "Voltar"); back.Clicked += async (_, _) => await Shell.Current.GoToAsync("..");
        var title = MarketplaceTheme.Text("Anúncio", 13, bold: true); title.HorizontalTextAlignment = TextAlignment.Center; title.VerticalOptions = LayoutOptions.Center;
        var favorite = new ImageButton { Source = "icon_heart.png", WidthRequest = 48, HeightRequest = 48, Padding = 12, BackgroundColor = Colors.Transparent, IsEnabled = false };
        SemanticProperties.SetDescription(favorite, "Favoritar anúncio: ainda indisponível");
        var header = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = 48 }, new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = 48 } } };
        header.Add(back); header.Add(title, 1); header.Add(favorite, 2); return header;
    }

    private async void OpenComparison(object? sender, SelectionChangedEventArgs args)
    {
        var listing = args.CurrentSelection.OfType<MarketplaceListingPresentation>().FirstOrDefault(); _comparisons.SelectedItem = null;
        if (listing is null || listing.Id == _viewModel.Listing?.Id || _navigating) return;
        _navigating = true;
        try { await Shell.Current.GoToAsync($"marketplace-listing?listingId={listing.Id}"); } finally { _navigating = false; }
    }
    private async void OpenSeller(object? sender, EventArgs args)
    {
        if (_viewModel.Listing is not { } listing || _navigating) return;
        _navigating = true;
        try { await Shell.Current.GoToAsync($"marketplace-seller?sellerUserId={listing.SellerUserId}"); } finally { _navigating = false; }
    }
    private async void Buy(object? sender, EventArgs args)
    {
        if (!_viewModel.CanBuy || _viewModel.Listing is not { Status: "active" } listing || _navigating) return;
        _navigating = true;
        try
        {
            if (!_session.IsAuthenticated && Shell.Current is AppShell shell) { await shell.ShowLoginAsync(); return; }
            await Shell.Current.GoToAsync("experience", new Dictionary<string, object> { ["checkoutListing"] = listing });
        }
        finally { _navigating = false; }
    }
    private async void EnlargePhoto(object? sender, EventArgs args)
    {
        if (_viewModel.SelectedPhoto is not { } photo || _navigating) return;
        _navigating = true;
        try
        {
            var back = MarketplaceTheme.Action("Voltar às fotos"); back.Clicked += async (_, _) => await Shell.Current.Navigation.PopAsync();
            var layout = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star }, new RowDefinition { Height = GridLength.Auto } } };
            layout.Add(back);
            layout.Add(new Image { Source = photo.Url, Aspect = Aspect.AspectFit }, 0, 1);
            layout.Add(MarketplaceTheme.Text(photo.Description, 13, secondary: true), 0, 2);
            var page = new ContentPage { Title = "Foto do anúncio", BackgroundColor = MarketplaceTheme.Background, Content = layout, Padding = 16 };
            Shell.SetNavBarIsVisible(page, false); Shell.SetTabBarIsVisible(page, false);
            await Shell.Current.Navigation.PushAsync(page);
        }
        finally { _navigating = false; }
    }
}
