using Microsoft.Maui.Controls.Shapes;

namespace Vaulta.App.Views.Components;

/// <summary>
/// Reusable native chart component for portfolio, card detail, and filtered collection views.
/// Renders a bar chart with color-coded trend indicators using only MAUI primitives (no SkiaSharp).
/// </summary>
public sealed class VaultaChart : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IReadOnlyList<ChartPoint>), typeof(VaultaChart), null, propertyChanged: OnDataChanged);

    public static readonly BindableProperty ChartHeightProperty =
        BindableProperty.Create(nameof(ChartHeight), typeof(double), typeof(VaultaChart), 120.0, propertyChanged: OnDataChanged);

    public static readonly BindableProperty UpColorProperty =
        BindableProperty.Create(nameof(UpColor), typeof(Color), typeof(VaultaChart), Color.FromArgb("#22D3EE"), propertyChanged: OnDataChanged);

    public static readonly BindableProperty DownColorProperty =
        BindableProperty.Create(nameof(DownColor), typeof(Color), typeof(VaultaChart), Color.FromArgb("#FB7185"), propertyChanged: OnDataChanged);

    public IReadOnlyList<ChartPoint>? ItemsSource
    {
        get => (IReadOnlyList<ChartPoint>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public double ChartHeight
    {
        get => (double)GetValue(ChartHeightProperty);
        set => SetValue(ChartHeightProperty, value);
    }

    public Color UpColor
    {
        get => (Color)GetValue(UpColorProperty);
        set => SetValue(UpColorProperty, value);
    }

    public Color DownColor
    {
        get => (Color)GetValue(DownColorProperty);
        set => SetValue(DownColorProperty, value);
    }

    private static void OnDataChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is VaultaChart chart)
            chart.Render();
    }

    private void Render()
    {
        Content = null;
        var data = ItemsSource;
        if (data is null || data.Count == 0)
        {
            Content = new Label
            {
                Text = "Sem dados disponíveis",
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                HeightRequest = ChartHeight
            };
            return;
        }

        var values = data.Select(p => p.Value).ToArray();
        var min = values.Min();
        var max = values.Max();
        var range = max - min > 0 ? max - min : 1;

        var grid = new Grid
        {
            HeightRequest = ChartHeight,
            ColumnSpacing = 4,
            Padding = new Thickness(8, 4, 8, 4),
            VerticalOptions = LayoutOptions.End,
            HorizontalOptions = LayoutOptions.Fill
        };

        for (int i = 0; i < values.Length; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (int i = 0; i < values.Length; i++)
        {
            var norm = (values[i] - min) / range;
            var barHeight = Math.Max(ChartHeight * norm, 2);
            var isUp = i == 0 || values[i] >= values[i - 1];

            var bar = new Border
            {
                WidthRequest = 8,
                HeightRequest = barHeight,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.End,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) },
                Background = new SolidColorBrush(isUp ? UpColor : DownColor)
            };

            grid.Add(bar, i, 0);
        }

        Content = new Border
        {
            Content = grid,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(12, 8, 12, 12)
        };
        Content.SetDynamicResource(Border.BackgroundColorProperty, "SurfaceElevated");
        Content.SetDynamicResource(Border.StrokeProperty, "BorderSubtle");
    }
}

public sealed record ChartPoint(DateTimeOffset Date, double Value, string? Label = null);