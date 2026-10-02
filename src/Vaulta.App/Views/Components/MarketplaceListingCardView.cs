using Microsoft.Maui.Controls.Shapes;
using Vaulta.App.Core.Marketplace;

namespace Vaulta.App.Views.Components;

public sealed class MarketplaceListingCardView : ContentView
{
    public MarketplaceListingCardView()
    {
        var artwork = new Image { Aspect = Aspect.AspectFit, HeightRequest = 156 };
        artwork.SetBinding(Image.SourceProperty, nameof(MarketplaceListingPresentation.ImageUrl));
        var placeholder = MarketplaceTheme.Text("Sem imagem\ndisponível", 11, secondary: true);
        placeholder.HorizontalTextAlignment = TextAlignment.Center;
        placeholder.VerticalTextAlignment = TextAlignment.Center;
        placeholder.SetBinding(IsVisibleProperty, new Binding(nameof(MarketplaceListingPresentation.ImageUrl), converter: new MissingImageConverter()));
        var imageSlot = new Border
        {
            HeightRequest = 156, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new Grid { Children = { artwork, placeholder } }
        };
        var name = MarketplaceTheme.Text(null, 13, bold: true);
        name.MaxLines = 2; name.LineBreakMode = LineBreakMode.TailTruncation;
        name.SetBinding(Label.TextProperty, nameof(MarketplaceListingPresentation.Name));
        var price = MarketplaceTheme.Text(null, 18, bold: true);
        price.SetBinding(Label.TextProperty, nameof(MarketplaceListingPresentation.Price));
        var metadata = MarketplaceTheme.Text(null, 11, secondary: true);
        metadata.SetBinding(Label.TextProperty, nameof(MarketplaceListingPresentation.Metadata));
        var seller = MarketplaceTheme.Text(null, 11, secondary: true);
        seller.SetBinding(Label.TextProperty, nameof(MarketplaceListingPresentation.Seller));
        var reference = MarketplaceTheme.Text(null, 10, secondary: true);
        reference.SetBinding(Label.TextProperty, nameof(MarketplaceListingPresentation.ImageDescription));
        reference.SetBinding(IsVisibleProperty, nameof(MarketplaceListingPresentation.IsCatalogReference));
        Content = new VerticalStackLayout
        {
            Spacing = 4, Padding = new Thickness(0, 0, 0, 8),
            Children = { imageSlot, name, price, metadata, seller, reference }
        };
        SetBinding(SemanticProperties.DescriptionProperty, new Binding(nameof(MarketplaceListingPresentation.AccessibleDescription)));
    }

    private sealed class MissingImageConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not string text || string.IsNullOrWhiteSpace(text);
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
