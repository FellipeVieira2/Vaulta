using System.Globalization;
using Vaulta.App.ViewModels;

namespace Vaulta.App.Views;

public sealed partial class ExperiencePage
{
    private static readonly NotConverter Not = new();

    private void BindInteraction(VisualElement view) => view.SetBinding(IsEnabledProperty,
        new Binding(nameof(ExperienceViewModel.IsBusy), converter: Not, source: _viewModel));

    private static View ComposeOnboarding(Grid illustrationAndCopy, Grid footer)
    {
        illustrationAndCopy.RowDefinitions[3].Height = GridLength.Auto;
        illustrationAndCopy.RowSpacing = 20;
        illustrationAndCopy.MaximumWidthRequest = 480;
        illustrationAndCopy.HorizontalOptions = LayoutOptions.Center;
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        root.Add(new ScrollView { Content = illustrationAndCopy });
        footer.Margin = new Thickness(24, 0);
        footer.MaximumWidthRequest = 480;
        root.Add(footer, 0, 1);
        return root;
    }

    private void UpdateConditionButtons()
    {
        foreach (var button in _content.Children.OfType<Button>().Where(x => x.ClassId is not null))
        {
            var selected = button.ClassId == _viewModel.SelectedCondition;
            button.SetDynamicResource(Button.BackgroundColorProperty, selected ? "BrandPrimary" : "SurfaceElevated");
            button.SetDynamicResource(Button.TextColorProperty, selected ? "TextInverse" : "TextPrimary");
            SemanticProperties.SetDescription(button, button.ClassId + (selected ? ", selecionada" : ""));
        }
    }

    private sealed class NotConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
