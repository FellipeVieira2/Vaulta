using Vaulta.App.Core.Marketplace;

namespace Vaulta.App.Views.Components;

public sealed class MarketplaceComparisonRowView : ContentView
{
    public MarketplaceComparisonRowView()
    {
        var seller = MarketplaceTheme.Text(null, 11);
        seller.SetBinding(Label.TextProperty, nameof(MarketplaceListingPresentation.Seller));
        var detail = MarketplaceTheme.Text(null, 11, secondary: true);
        detail.SetBinding(Label.TextProperty, nameof(MarketplaceListingPresentation.Metadata));
        var price = MarketplaceTheme.Text(null, 13, bold: true);
        price.SetBinding(Label.TextProperty, nameof(MarketplaceListingPresentation.Price));
        price.VerticalOptions = LayoutOptions.Center;
        var left = new VerticalStackLayout { Spacing = 4, Children = { seller, detail } };
        var row = new Grid
        {
            MinimumHeightRequest = 48, ColumnSpacing = 8, Padding = new Thickness(0, 4),
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } }
        };
        row.Add(left); row.Add(price, 1);
        Content = row;
        SetBinding(SemanticProperties.DescriptionProperty, new Binding(nameof(MarketplaceListingPresentation.AccessibleDescription)));
    }
}
