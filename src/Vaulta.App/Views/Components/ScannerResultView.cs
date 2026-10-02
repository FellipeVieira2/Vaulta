using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Vaulta.App.Core.Catalog;

namespace Vaulta.App.Views.Components;

public sealed class ScannerResultView : ContentView
{
    public ScannerResultView(ScannerSessionCard card)
    {
        var image = new Image { Source = card.ArtworkUrl, WidthRequest = 68, HeightRequest = 96, Aspect = Aspect.AspectFit };
        SemanticProperties.SetDescription(image, $"Referência do catálogo: {card.Name}");
        var body = new VerticalStackLayout { Spacing = 4 };
        body.Children.Add(MarketplaceTheme.Text(card.Name, 13, true));
        body.Children.Add(MarketplaceTheme.Text($"{card.SetName} · {card.CollectorNumber} · {card.VariantName}", 11, secondary: true));
        body.Children.Add(MarketplaceTheme.Text(card.MarketValue is { } q ? q.AmountBrl.ToString("C2", CultureInfo.GetCultureInfo("pt-BR")) : "Sem cotação", 18, true));
        body.Children.Add(MarketplaceTheme.Text(card.MarketValue is { } value ? $"{value.Source} · {value.QuotedAt.ToLocalTime():dd/MM HH:mm}" : "Você pode informar seu preço de venda.", 11, secondary: true));
        body.Children.Add(MarketplaceTheme.Text("Referência de mercado · não é preço pedido", 11, secondary: true));
        var grid = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } }; grid.Add(image); grid.Add(body, 1, 0);
        Content = new Border { Padding = 12, BackgroundColor = MarketplaceTheme.Surface, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 }, Content = grid };
    }
}
