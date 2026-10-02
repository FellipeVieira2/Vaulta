using Microsoft.Maui.Controls.Shapes;

namespace Vaulta.App.Views.Components;

/// <summary>Accessible native counterpart of the Figma Input Field component.</summary>
internal sealed class AuthenticationField : VerticalStackLayout
{
    public Entry Input { get; }
    public Grid InputLayout { get; }

    public AuthenticationField(string title, string placeholder, string valuePath, string errorPath, string hasErrorPath)
    {
        Spacing = 8;
        Children.Add(MarketplaceTheme.Text(title, 13, bold: true));
        Input = new Entry
        {
            Placeholder = placeholder, PlaceholderColor = MarketplaceTheme.Secondary,
            BackgroundColor = Colors.Transparent, TextColor = MarketplaceTheme.Primary,
            FontFamily = MarketplaceTheme.RegularFont, FontSize = 14,
            MinimumHeightRequest = 48, Margin = new Thickness(12, 0),
            FontAutoScalingEnabled = true, IsTextPredictionEnabled = false, IsSpellCheckEnabled = false
        };
        Input.SetBinding(Entry.TextProperty, valuePath, BindingMode.TwoWay);
        SemanticProperties.SetDescription(Input, title);
        InputLayout = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        InputLayout.Add(Input);
        var border = new Border
        {
            BackgroundColor = MarketplaceTheme.Surface, Stroke = MarketplaceTheme.Border,
            StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = InputLayout
        };
        border.Triggers.Add(new DataTrigger(typeof(Border))
        {
            Binding = new Binding(hasErrorPath), Value = true,
            Setters = { new Setter { Property = Border.StrokeProperty, Value = MarketplaceTheme.Error } }
        });
        Children.Add(border);
        var error = MarketplaceTheme.Text(null, 12);
        error.TextColor = MarketplaceTheme.Error;
        error.SetBinding(Label.TextProperty, errorPath);
        error.SetBinding(IsVisibleProperty, hasErrorPath);
        Children.Add(error);
    }
}
