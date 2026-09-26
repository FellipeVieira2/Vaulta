using Microsoft.Maui.Controls.Shapes;
using Vaulta.App.ViewModels;

namespace Vaulta.App.Views;

public sealed partial class ExperiencePage
{
    private void AddCards(ScreenDefinition screen)
    {
        _content.Children.Add(CreateLabel("Prévia · cartas e valores demonstrativos", "CaptionTextStyle", "TextSecondary"));
        var gallery = new Grid { ColumnSpacing = 12, RowSpacing = 16 };
        var tiles = screen.Cards!.Select(card => CreateCardTile(screen, card)).ToArray();
        var currentColumns = 0;
        void Arrange(double width)
        {
            var columns = (screen.Id is "catalog" or "search-results" or "collection") && width >= 340 ? 2 : 1;
            if (columns == currentColumns) return;
            currentColumns = columns;
            gallery.Children.Clear();
            gallery.ColumnDefinitions.Clear();
            gallery.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) gallery.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (var i = 0; i < tiles.Length; i++)
            {
                if (i % columns == 0) gallery.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                gallery.Add(tiles[i], i % columns, i / columns);
            }
        }
        Arrange(0);
        gallery.SizeChanged += (_, _) => Arrange(gallery.Width);
        _content.Children.Add(gallery);
    }

    private Border CreateCardTile(ScreenDefinition screen, ScreenCard card)
    {
        var details = new VerticalStackLayout { Spacing = 8 };
        if (screen.Id is "catalog" or "search-results" or "collection")
            details.Children.Add(CreateCardArtwork(card.ArtworkUrl, card.Title, 180));
        details.Children.Add(CreateLabel(card.Game, "CaptionTextStyle", "BrandPrimary"));
        details.Children.Add(CreateLabel(card.Title, "TitleTextStyle"));
        details.Children.Add(CreateLabel(card.Detail, "BodySmallTextStyle", "TextSecondary"));
        details.Children.Add(CreateLabel(card.Price, "LabelTextStyle"));
        if (card.Change is not null)
            details.Children.Add(CreateLabel(card.Change, "CaptionTextStyle", card.Change.StartsWith('-') ? "StatusError" : "StatusSuccess"));
        var open = CreateButton("Ver prévia", false);
        SemanticProperties.SetDescription(open, $"Ver prévia de {card.Title}");
        open.Clicked += async (_, _) => await _viewModel.NavigateCommand.ExecuteAsync(screen.Id switch
        {
            "collection" => "collection-item",
            "scan-candidates" => "scan-confirmed",
            "market" => "listing-detail",
            _ => "card-detail"
        });
        details.Children.Add(open);
        return CreateSurface(details);
    }

    private View CreateCardArtwork(string? artworkUrl, string title, double height)
    {
        // The placeholder remains behind the image for offline and missing-artwork states.
        var frame = new Grid { HeightRequest = height, BackgroundColor = ColorResource("SurfaceElevated") };
        var placeholder = CreateLabel("Artwork\nindisponível", "CaptionTextStyle", "TextSecondary");
        placeholder.HorizontalTextAlignment = TextAlignment.Center;
        placeholder.VerticalTextAlignment = TextAlignment.Center;
        frame.Add(placeholder);
        if (artworkUrl is not null)
        {
            var image = new Image
            {
                Source = new UriImageSource { Uri = new Uri(artworkUrl), CachingEnabled = false },
                Aspect = Aspect.AspectFit,
                BackgroundColor = Colors.Transparent
            };
            SemanticProperties.SetDescription(image, $"Artwork de {title}");
            frame.Add(image);
        }
        return new Border { Padding = 8, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 12 }, Content = frame };
    }
}
