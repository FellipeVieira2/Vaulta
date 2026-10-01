using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace Vaulta.App.Views;

public sealed partial class ExperiencePage
{
    private View CreateScannerArtwork(string? artworkUrl, string title)
    {
        var artwork = CreateCardArtwork(artworkUrl, title, 330);
        return new Border
        {
            Padding = new Thickness(20, 24),
            Stroke = new SolidColorBrush(ColorResource("BrandMuted")), StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(24) },
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(ColorResource("BrandMuted"), 0),
                new GradientStop(ColorResource("SurfaceDefault"), 0.45f),
                new GradientStop(ColorResource("BackgroundPrimary"), 1)
            }, new Point(0, 0), new Point(1, 1)),
            Content = artwork,
            Shadow = new Shadow { Brush = new SolidColorBrush(ColorResource("BrandPrimary")), Opacity = 0.12f, Radius = 24, Offset = new Point(0, 8) }
        };
    }

    private View CreateScannerGuide()
    {
        var guide = new VerticalStackLayout { Spacing = 16, Padding = new Thickness(24), VerticalOptions = LayoutOptions.Center };
        guide.Children.Add(CreateLabel("UMA CARTA. UM NOVO ACHADO.", "CaptionTextStyle", "BrandPrimary"));
        guide.Children.Add(new Border
        {
            WidthRequest = 180, HeightRequest = 245, HorizontalOptions = LayoutOptions.Center,
            Stroke = new SolidColorBrush(ColorResource("BrandPrimary")), StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
            BackgroundColor = ColorResource("SurfaceElevated"),
            Content = new Label { Text = "VAULTA\n\nEnquadre a carta inteira", TextColor = ColorResource("TextSecondary"),
                FontSize = 16, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }
        });
        guide.Children.Add(CreateLabel("Boa luz, carta reta e sem reflexos.\nO resto fica com a Vaulta.", "BodySmallTextStyle", "TextSecondary"));
        return CreateSurface(guide);
    }

    private void AddScannerCardDetails()
    {
        if (_viewModel.ScreenId == "scanner-card-detail")
        {
            var footer = new VerticalStackLayout { Padding = new Thickness(24, 12, 24, 20), BackgroundColor = ColorResource("BackgroundPrimary") };
            var add = CreateButton("Adicionar ao meu Vault", true);
            add.CornerRadius = 28;
            footer.Children.Add(add);
            add.Clicked += async (_, _) => await _viewModel.NavigateCommand.ExecuteAsync("add-to-collection");
            var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
            Content = null;
            layout.Add(_scroll, 0, 0);
            layout.Add(footer, 0, 1);
            Content = layout;
        }
        if (_viewModel.ScannerCardDetails is not { } details) return;
        var br = CultureInfo.GetCultureInfo("pt-BR");
        _content.Children.Add(CreateLabel("VALOR DE MERCADO EM REAIS", "CaptionTextStyle"));
        if (details.MarketQuotes.Count == 0)
            _content.Children.Add(CreateLabel("Valor de mercado indisponível", "BodyTextStyle", "TextTertiary"));
        foreach (var quote in details.MarketQuotes)
        {
            var price = new VerticalStackLayout { Spacing = 8 };
            price.Children.Add(CreateLabel(quote.VariantName, "TitleTextStyle"));
            var marketValue = CreateLabel(quote.MarketValueBrl.ToString("C2", br), "H1TextStyle", "BrandPrimary");
            marketValue.FontSize = 32;
            price.Children.Add(marketValue);
            price.Children.Add(CreateLabel($"{quote.Source} · atualizado em {quote.UpdatedAt.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy}", "CaptionTextStyle", "TextSecondary"));
            price.Children.Add(CreateLabel($"Câmbio PTAX de {quote.ExchangeRateAt.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy}", "CaptionTextStyle", "TextSecondary"));
            foreach (var comparison in quote.Comparisons)
            {
                var difference = comparison.DifferenceBrl.ToString("C2", br);
                var percent = comparison.DifferencePercent.ToString("N2", br);
                var sign = comparison.DifferenceBrl > 0 ? "+" : "";
                var period = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Padding = new Thickness(0, 8) };
                var average = new VerticalStackLayout { Spacing = 3 };
                average.Children.Add(CreateLabel($"MÉDIA · {comparison.Days} {(comparison.Days == 1 ? "DIA" : "DIAS")}", "CaptionTextStyle", "TextSecondary"));
                average.Children.Add(CreateLabel(comparison.AverageBrl.ToString("C2", br), "LabelTextStyle"));
                var change = new VerticalStackLayout { Spacing = 3, HorizontalOptions = LayoutOptions.End };
                var statusColor = comparison.DifferenceBrl < 0 ? "StatusError" : "StatusSuccess";
                change.Children.Add(CreateLabel($"{sign}{difference}", "LabelTextStyle", statusColor));
                change.Children.Add(CreateLabel($"{sign}{percent}% vs. média", "CaptionTextStyle", statusColor));
                period.Add(average, 0, 0); period.Add(change, 1, 0);
                price.Children.Add(period);
            }
            if (quote.Comparisons.Count == 0)
                price.Children.Add(CreateLabel("Comparação por período indisponível para esta variante.", "CaptionTextStyle", "TextTertiary"));
            _content.Children.Add(CreateSurface(price));
        }
        if (details.Notice is not null)
            _content.Children.Add(CreateLabel(details.Notice, "BodySmallTextStyle", "TextSecondary"));
        _content.Children.Add(CreateLabel("INFORMAÇÕES DA CARTA", "CaptionTextStyle"));
        foreach (var information in details.Information.Where(x => x.Key is not ("Nome" or "Número da carta" or "Expansão")))
        {
            var fact = new VerticalStackLayout { Spacing = 6 };
            fact.Children.Add(CreateLabel(information.Key, "CaptionTextStyle", "TextSecondary"));
            fact.Children.Add(CreateLabel(information.Value, "BodySmallTextStyle"));
            _content.Children.Add(CreateSurface(fact));
        }
        _content.Children.Add(CreateLabel($"Idioma: {details.Printing.Language}", "BodySmallTextStyle"));
    }
}
