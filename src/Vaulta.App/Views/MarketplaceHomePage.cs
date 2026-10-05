using Microsoft.Maui.Controls.Shapes;
using Vaulta.App.Core.Marketplace;
using Vaulta.App.ViewModels;
using Vaulta.App.Views.Components;

namespace Vaulta.App.Views;

/// <summary>Native, virtualized marketplace Home. Network and paging state live in App.Core.</summary>
public sealed class MarketplaceHomePage : ContentPage
{
    private readonly MarketplaceHomeViewModel _viewModel;
    private readonly Entry _search;
    private readonly CollectionView _list;
    private readonly ContentView _navigation = new();
    private readonly List<(Button Button, string? Code)> _gameButtons = [];
    private bool _searchActive;
    private readonly IMarketplaceProductClient _productClient;
    private bool _openingListing;

    public MarketplaceHomePage(MarketplaceHomeViewModel viewModel, IMarketplaceProductClient productClient)
    {
        _viewModel = viewModel; _productClient = productClient;
        BindingContext = viewModel;
        Title = "Início";
        BackgroundColor = MarketplaceTheme.Background;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _search = new Entry
        {
            Placeholder = "Buscar carta, set ou número", PlaceholderColor = MarketplaceTheme.Secondary,
            TextColor = MarketplaceTheme.Primary, BackgroundColor = Colors.Transparent,
            FontSize = 13, MaxLength = 160, ReturnType = ReturnType.Search,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            MinimumHeightRequest = 44
        };
        _search.SetBinding(Entry.TextProperty, nameof(MarketplaceHomeViewModel.SearchText), BindingMode.TwoWay);
        _search.SetBinding(Entry.ReturnCommandProperty, nameof(MarketplaceHomeViewModel.SearchCommand));
        SemanticProperties.SetDescription(_search, "Buscar cartas por nome, set ou número da carta");

        _list = new CollectionView
        {
            BackgroundColor = MarketplaceTheme.Background,
            ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 16, VerticalItemSpacing = 0 },
            ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems,
            SelectionMode = SelectionMode.Single, RemainingItemsThreshold = 4,
            Margin = new Thickness(16, 0),
            ItemTemplate = new DataTemplate(() => new MarketplaceProductCardView()),
            Header = BuildHeader(), Footer = BuildFooter()
        };
        _list.SetBinding(ItemsView.ItemsSourceProperty, nameof(MarketplaceHomeViewModel.Products));
        _list.SetBinding(ItemsView.RemainingItemsThresholdReachedCommandProperty, nameof(MarketplaceHomeViewModel.LoadMoreCommand));
        _list.SelectionChanged += OpenListing;
        SemanticProperties.SetDescription(_list, "Cartas à venda. Selecione uma carta para ver as ofertas desta impressão e acabamento");

        var layout = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Star }, new RowDefinition { Height = GridLength.Auto } } };
        layout.Add(_list);
        layout.Add(_navigation, 0, 1);
        Content = layout;
        UpdateNavigation();
        UpdateGames();
    }

    public void ConfigureSearch()
    {
        _searchActive = true;
        Title = "Buscar";
        UpdateNavigation();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.PropertyChanged += ViewModelChanged;
        UpdateGames();
        if (_searchActive) Dispatcher.Dispatch(() => _search.Focus());
        await _viewModel.ActivateAsync();
    }

    protected override void OnDisappearing()
    {
        _viewModel.PropertyChanged -= ViewModelChanged;
        _viewModel.Deactivate();
        base.OnDisappearing();
    }

    private View BuildHeader()
    {
        var header = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 12, 0, 8) };
        var brand = MarketplaceTheme.Text("VAULTA", 20, bold: true);
        brand.VerticalOptions = LayoutOptions.Center;
        SemanticProperties.SetHeadingLevel(brand, SemanticHeadingLevel.Level1);
        var profile = new ImageButton
        {
            Source = "tab_profile.svg", BackgroundColor = Colors.Transparent, Padding = 12,
            WidthRequest = 48, HeightRequest = 48, HorizontalOptions = LayoutOptions.End
        };
        SemanticProperties.SetDescription(profile, "Abrir meu perfil");
        profile.Clicked += async (_, _) => await NavigateAsync("profile");
        var top = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } } };
        top.Add(brand); top.Add(profile, 1);
        header.Children.Add(top);
        header.Children.Add(MarketplaceTheme.Text("Seu próximo card começa aqui.", 11, secondary: true));
        var searchRow = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition { Width = 24 }, new ColumnDefinition { Width = GridLength.Star } } };
        searchRow.Add(new Image { Source = "icon_search.svg", WidthRequest = 24, HeightRequest = 24, VerticalOptions = LayoutOptions.Center });
        searchRow.Add(_search, 1);
        header.Children.Add(new Border
        {
            Stroke = MarketplaceTheme.Border, StrokeThickness = 1, BackgroundColor = MarketplaceTheme.Surface,
            StrokeShape = new RoundRectangle { CornerRadius = 8 }, Padding = new Thickness(12, 0), Content = searchRow
        });

        var games = new HorizontalStackLayout { Spacing = 4 };
        AddGame(games, "Pokémon", "pokemon");
        AddGame(games, "Yu-Gi-Oh!", "yugioh", available: false);
        AddGame(games, "One Piece", "onepiece", available: false);
        AddGame(games, "Magic", "magic", available: false);

        header.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = games });

        var filters = new HorizontalStackLayout { Spacing = 8 };
        foreach (var title in new[] { "Set", "Variante", "Condição", "Filtros" })
        {
            var filter = MarketplaceTheme.Action(title); filter.BackgroundColor = MarketplaceTheme.Surface;
            filter.TextColor = MarketplaceTheme.Primary;
            filter.Clicked += async (_,_) => await Navigation.PushModalAsync(new MarketplaceFiltersPage(_productClient,
                _viewModel.CurrentFilters, _viewModel.ApplyFiltersAsync));
            filters.Children.Add(filter);
        }
        header.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = filters, HorizontalScrollBarVisibility = ScrollBarVisibility.Never });
        var section = MarketplaceTheme.Text(null, 16, bold: true);
        section.SetBinding(Label.TextProperty, nameof(MarketplaceHomeViewModel.SectionTitle));
        section.VerticalOptions = LayoutOptions.Center;
        SemanticProperties.SetHeadingLevel(section, SemanticHeadingLevel.Level2);
        var sort = new Picker
        {
            Title = "Ordenar cartas", ItemsSource = new[] { "Mais recentes", "Menor preço", "Maior preço" },
            SelectedIndex = 0, FontSize = 11, TextColor = MarketplaceTheme.Secondary,
            BackgroundColor = Colors.Transparent, WidthRequest = 144, MinimumHeightRequest = 48
        };
        SemanticProperties.SetDescription(sort, "Ordenação das cartas");
        sort.SelectedIndexChanged += (_, _) => _viewModel.SelectedSort = sort.SelectedIndex switch { 1 => "price_asc", 2 => "price_desc", _ => "newest" };
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MarketplaceHomeViewModel.SelectedSort))
                sort.SelectedIndex = _viewModel.SelectedSort switch { "price_asc" => 1, "price_desc" => 2, _ => 0 };
        };
        var sectionRow = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } } };
        sectionRow.Add(section); sectionRow.Add(sort, 1);
        header.Children.Add(sectionRow);
        var count = MarketplaceTheme.Text(null, 11, secondary: true);
        count.SetBinding(Label.TextProperty, nameof(MarketplaceHomeViewModel.ResultSummary));
        header.Children.Add(count);
        var loading = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(0, 12) };
        var activity = new ActivityIndicator { Color = MarketplaceTheme.Brand, WidthRequest = 24, HeightRequest = 24 };
        activity.SetBinding(ActivityIndicator.IsRunningProperty, nameof(MarketplaceHomeViewModel.IsLoading));
        loading.Children.Add(activity); loading.Children.Add(MarketplaceTheme.Text("Carregando cartas…", secondary: true));
        loading.SetBinding(IsVisibleProperty, nameof(MarketplaceHomeViewModel.IsLoading));
        header.Children.Add(loading);
        header.Children.Add(BuildProblem());
        var empty = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 24) };
        empty.Children.Add(MarketplaceTheme.Text("Nenhuma carta encontrada", 16, bold: true));
        empty.Children.Add(MarketplaceTheme.Text("Tente outra busca ou limpe os filtros.", secondary: true));
        var clear = MarketplaceTheme.Action("Limpar filtros");
        clear.SetBinding(Button.CommandProperty, nameof(MarketplaceHomeViewModel.ClearFiltersCommand));
        empty.Children.Add(clear);
        empty.SetBinding(IsVisibleProperty, nameof(MarketplaceHomeViewModel.IsEmpty));
        header.Children.Add(empty);
        return header;
    }

    private View BuildProblem(string visibilityProperty = nameof(MarketplaceHomeViewModel.ShowInitialProblem))
    {
        var problem = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 16) };
        var title = MarketplaceTheme.Text(null, 16, bold: true);
        title.SetBinding(Label.TextProperty, nameof(MarketplaceHomeViewModel.StatusTitle));
        var message = MarketplaceTheme.Text(null, secondary: true);
        message.SetBinding(Label.TextProperty, nameof(MarketplaceHomeViewModel.StatusMessage));
        var retry = MarketplaceTheme.Action("Tentar novamente");
        retry.SetBinding(Button.CommandProperty, nameof(MarketplaceHomeViewModel.RetryCommand));
        problem.Children.Add(title); problem.Children.Add(message); problem.Children.Add(retry);
        problem.SetBinding(IsVisibleProperty, visibilityProperty);
        return problem;
    }

    private View BuildFooter()
    {
        var footer = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(0, 8, 0, 16) };
        footer.Children.Add(BuildProblem(nameof(MarketplaceHomeViewModel.ShowFooterProblem)));
        var loading = new ActivityIndicator { Color = MarketplaceTheme.Brand, HeightRequest = 36 };
        loading.SetBinding(ActivityIndicator.IsRunningProperty, nameof(MarketplaceHomeViewModel.IsLoadingMore));
        loading.SetBinding(IsVisibleProperty, nameof(MarketplaceHomeViewModel.IsLoadingMore));
        footer.Children.Add(loading);
        var more = MarketplaceTheme.Action("Carregar mais cartas");
        more.SetBinding(Button.CommandProperty, nameof(MarketplaceHomeViewModel.LoadMoreCommand));
        more.SetBinding(IsVisibleProperty, nameof(MarketplaceHomeViewModel.HasMore));
        footer.Children.Add(more);
        var all = new Grid { MinimumHeightRequest = 48, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = 24 } } };
        var label = MarketplaceTheme.Text("Ver todas as cartas", 13, bold: true);
        label.TextColor = MarketplaceTheme.Brand; label.VerticalOptions = LayoutOptions.Center;
        all.Add(label);
        all.Add(new Image { Source = "icon_chevron_right.svg", WidthRequest = 24, HeightRequest = 24, VerticalOptions = LayoutOptions.Center }, 1);
        var allAction = MarketplaceTheme.Action("");
        SemanticProperties.SetDescription(allAction, "Ver todas as cartas. Limpar busca e filtros");
        allAction.SetBinding(Button.CommandProperty, nameof(MarketplaceHomeViewModel.ClearFiltersCommand));
        all.Add(allAction); Grid.SetColumnSpan(allAction, 2);
        footer.Children.Add(all);
        var catalog = MarketplaceTheme.Action("Catálogo de cartas");
        SemanticProperties.SetDescription(catalog, "Buscar cartas no catálogo para adicionar à coleção");
        catalog.Clicked += async (_, _) => await Shell.Current.GoToAsync("experience?screen=catalog");
        footer.Children.Add(catalog);
        var refresh = MarketplaceTheme.Action("Atualizar cartas");
        refresh.SetBinding(Button.CommandProperty, nameof(MarketplaceHomeViewModel.RefreshCommand));
        footer.Children.Add(refresh);
        return footer;
    }

    private void AddGame(HorizontalStackLayout row, string title, string? code, bool available = true)
    {
        var button = MarketplaceTheme.Action(title);
        button.FontSize = 11; button.FontAttributes = FontAttributes.None;
        button.BorderWidth = 1; button.BorderColor = MarketplaceTheme.Border; button.CornerRadius = 0;
        button.BackgroundColor = MarketplaceTheme.Surface;
        button.TextColor = MarketplaceTheme.Secondary;
        button.MinimumHeightRequest = 48; button.Padding = new Thickness(14, 0); button.IsEnabled = available;
        SemanticProperties.SetDescription(button, available ? $"Filtrar anúncios: {title}" : $"{title}: catálogo ainda indisponível");
        button.Clicked += (_, _) => _viewModel.SelectedGameCode = code;
        row.Children.Add(button);
        if (available) _gameButtons.Add((button, code));
    }

    private void ViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MarketplaceHomeViewModel.SelectedGameCode)) UpdateGames();
    }

    private void UpdateGames()
    {
        foreach (var (button, code) in _gameButtons)
        {
            var selected = code == _viewModel.SelectedGameCode;
            button.BorderColor = selected ? MarketplaceTheme.Brand : MarketplaceTheme.Border;
            button.TextColor = selected ? MarketplaceTheme.Primary : MarketplaceTheme.Secondary;
            SemanticProperties.SetHint(button, selected ? "Filtro selecionado" : "Toque para selecionar");
        }
    }

    private void UpdateNavigation() => _navigation.Content = new MarketplaceBottomNavigation(NavigateAsync, _searchActive);

    private static Task NavigateAsync(string route) => route == "sell" && Shell.Current is AppShell shell
        ? shell.OpenSellAsync() : Shell.Current.GoToAsync($"//main/{route}/{route}-page");

    private async void OpenListing(object? sender, SelectionChangedEventArgs args)
    {
        var listing = args.CurrentSelection.OfType<MarketplaceProductPresentation>().FirstOrDefault();
        _list.SelectedItem = null;
        if (listing is null || _openingListing) return;
        _openingListing = true;
        try { await Shell.Current.GoToAsync($"marketplace-product?printingId={listing.PrintingId}&variantKey={listing.VariantId?.ToString() ?? "none"}"); }
        finally { _openingListing = false; }
    }
}
