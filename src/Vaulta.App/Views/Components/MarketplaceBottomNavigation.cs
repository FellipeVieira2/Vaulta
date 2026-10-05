namespace Vaulta.App.Views.Components;

public sealed class MarketplaceBottomNavigation : ContentView
{
    public MarketplaceBottomNavigation(Func<string, Task> navigate, bool searchActive)
    {
        var buttons = new Grid { ColumnSpacing = 0, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Star } } };
        var actions = new[]
        {
            ("Início", "home", "tab_home.svg"), ("Buscar", "search", "icon_search.svg"),
            ("Vender", "sell", "icon_plus.svg"), ("Coleção", "collection", "tab_collection.svg"), ("Perfil", "profile", "tab_profile.svg")
        };
        for (var index = 0; index < actions.Length; index++)
        {
            var (title, route, icon) = actions[index];
            var button = MarketplaceTheme.Action(title);
            button.FontSize = 11; button.FontAttributes = FontAttributes.None;
            button.TextColor = route == "sell" ? MarketplaceTheme.Background
                : route == (searchActive ? "search" : "home") ? MarketplaceTheme.Primary : MarketplaceTheme.Secondary;
            button.BackgroundColor = route == "sell" ? MarketplaceTheme.Brand : Colors.Transparent;
            button.ImageSource = icon;
            button.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Top, 2);
            button.Padding = 0; button.MinimumWidthRequest = 48; button.HeightRequest = 48;
            SemanticProperties.SetDescription(button, route == "sell" ? "Vender uma carta. Abrir scanner" : title);
            button.Clicked += async (_, _) => await navigate(route);
            buttons.Add(button, index);
        }
        Content = new Border
        {
            BackgroundColor = MarketplaceTheme.Surface, Stroke = MarketplaceTheme.Border, StrokeThickness = 1,
            Padding = new Thickness(8, 8, 8, 20), Content = buttons
        };
    }
}
