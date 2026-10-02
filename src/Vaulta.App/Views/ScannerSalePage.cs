using Microsoft.Maui.Controls.Shapes;
using Vaulta.App.ViewModels;
using Vaulta.App.Views.Components;

namespace Vaulta.App.Views;

public sealed class ScannerSalePage : ContentPage
{
    private readonly ScannerSaleViewModel _vm;
    private readonly VerticalStackLayout _editor = new() { Spacing = 12 };
    private readonly VerticalStackLayout _review = new() { Spacing = 12 };
    private readonly Button _submit;
    private Guid _sessionId;
    private Guid _scanId;

    public ScannerSalePage(ScannerSaleViewModel vm)
    {
        _vm = vm; BindingContext = vm; BackgroundColor = MarketplaceTheme.Background;
        Shell.SetNavBarIsVisible(this, false); Shell.SetTabBarIsVisible(this, false);
        var title = MarketplaceTheme.Text("Vender esta carta", 20, true);
        var back = MarketplaceTheme.Action("Voltar"); back.Clicked += async (_, _) => await Navigation.PopAsync();
        var header = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } }; header.Add(title); header.Add(back, 1, 0);
        var identity = new VerticalStackLayout { Spacing = 4 };
        var name = MarketplaceTheme.Text(null, 13, true); name.SetBinding(Label.TextProperty, "Card.Name"); identity.Children.Add(name);
        var details = MarketplaceTheme.Text(null, 11, secondary: true); details.SetBinding(Label.TextProperty, nameof(vm.CardDetails)); identity.Children.Add(details);
        var quote = MarketplaceTheme.Text(null, 18, true); quote.SetBinding(Label.TextProperty, nameof(vm.MarketReference)); identity.Children.Add(quote);
        var artwork = new Image { WidthRequest = 68, HeightRequest = 96, Aspect = Aspect.AspectFit }; artwork.SetBinding(Image.SourceProperty, "Card.ArtworkUrl");
        SemanticProperties.SetDescription(artwork, "Imagem de referência do catálogo; não é foto da unidade à venda.");
        var result = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } }; result.Add(artwork); result.Add(identity, 1, 0);
        var card = new Border { Content = result, Padding = 12, BackgroundColor = MarketplaceTheme.Surface, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 8 } };
        _editor.Children.Add(MarketplaceTheme.Text("Condição", 11, secondary: true));
        var condition = new Picker { Title = "Selecione a condição da unidade", ItemsSource = ScannerSaleViewModel.Conditions.ToArray(), TextColor = MarketplaceTheme.Primary, MinimumHeightRequest = 44 };
        condition.SetBinding(Picker.SelectedIndexProperty, nameof(vm.ConditionIndex), BindingMode.TwoWay); _editor.Children.Add(condition);
        _editor.Children.Add(MarketplaceTheme.Text("Seu preço (R$)", 11, secondary: true));
        var price = new Entry { Placeholder = "Ex.: 25,90", Keyboard = Keyboard.Numeric, TextColor = MarketplaceTheme.Primary, FontSize = 18, MinimumHeightRequest = 44 };
        price.SetBinding(Entry.TextProperty, nameof(vm.PriceText), BindingMode.TwoWay); _editor.Children.Add(price);
        _editor.Children.Add(MarketplaceTheme.Text("Referência de mercado não define seu preço.", 11, secondary: true));
        _editor.Children.Add(MarketplaceTheme.Text("Fotos da sua carta", 16, true));
        _editor.Children.Add(Photo("Frente", "FRONT", nameof(vm.FrontUrl))); _editor.Children.Add(Photo("Verso", "BACK", nameof(vm.BackUrl)));
        _editor.Children.Add(MarketplaceTheme.Text("Use fotos reais da unidade. O artwork do catálogo é referência.", 11, secondary: true));
        _editor.Children.Add(MarketplaceTheme.Text("Detalhes (opcional)", 11, secondary: true));
        var description = new Editor { Placeholder = "Descreva marcas, riscos ou outros detalhes.", AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 66, TextColor = MarketplaceTheme.Primary };
        description.SetBinding(Editor.TextProperty, nameof(vm.Description), BindingMode.TwoWay); _editor.Children.Add(description);
        _editor.Children.Add(MarketplaceTheme.Text("Rascunho privado. Só fica público após a revisão.", 11, secondary: true));
        var summary = MarketplaceTheme.Text(null, 18, true); summary.SetBinding(Label.TextProperty, nameof(vm.ReviewSummary)); _review.Children.Add(summary);
        _review.Children.Add(Photo("Frente", null, nameof(vm.FrontUrl))); _review.Children.Add(Photo("Verso", null, nameof(vm.BackUrl)));
        _review.Children.Add(MarketplaceTheme.Text("Confira as fotos, a condição e seu preço. Publicar torna o anúncio visível no Marketplace.", 13));
        var seller = new HorizontalStackLayout { Spacing = 8 };
        var check = new CheckBox(); check.SetBinding(CheckBox.IsCheckedProperty, nameof(vm.ActivateSeller), BindingMode.TwoWay);
        seller.Children.Add(check); seller.Children.Add(MarketplaceTheme.Text("Quero ativar meu perfil de vendedor", 13)); seller.SetBinding(IsVisibleProperty, nameof(vm.NeedsSeller)); _review.Children.Add(seller);
        var edit = MarketplaceTheme.Action("Editar antes de publicar"); edit.SetBinding(Button.CommandProperty, nameof(vm.EditCommand)); _review.Children.Add(edit);
        var open = MarketplaceTheme.Action("Abrir anúncio"); open.SetBinding(IsVisibleProperty, nameof(vm.IsPublished));
        open.SetBinding(Button.TextProperty, nameof(vm.PublishedActionLabel));
        open.Clicked += async (_, _) => { if (vm.IsSold) await Shell.Current.GoToAsync("experience?screen=orders"); else if (vm.PublishedId is { } id) await Shell.Current.GoToAsync($"marketplace-listing?listingId={id}"); }; _review.Children.Add(open);
        var body = new VerticalStackLayout { Padding = 16, Spacing = 12, Children = { header, card, _editor, _review } };
        var activity = new ActivityIndicator { Color = MarketplaceTheme.Brand }; activity.SetBinding(ActivityIndicator.IsRunningProperty, nameof(vm.IsBusy)); body.Children.Add(activity);
        var status = MarketplaceTheme.Text(null, 13); status.SetBinding(Label.TextProperty, nameof(vm.StatusMessage)); body.Children.Add(status);
        var retry = MarketplaceTheme.Action("Retomar / atualizar rascunho"); retry.SetBinding(Button.CommandProperty, nameof(vm.ReloadCommand)); body.Children.Add(retry);
        _submit = MarketplaceTheme.Action("Salvar e revisar"); _submit.BackgroundColor = MarketplaceTheme.Brand; _submit.TextColor = MarketplaceTheme.Background;
        var footer = new Grid { Padding = new Thickness(16, 12, 16, 20), BackgroundColor = MarketplaceTheme.Surface, Children = { _submit } };
        var root = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) } }; root.Add(new ScrollView { Content = body }); root.Add(footer, 0, 1); Content = root;
        vm.PropertyChanged += (_, _) => UpdateState(title, edit);
        UpdateState(title, edit);
    }

    public void Configure(Guid sessionId, Guid scanId) { _sessionId = sessionId; _scanId = scanId; }
    protected override async void OnAppearing() { base.OnAppearing(); await _vm.ActivateAsync(_sessionId, _scanId); }
    protected override void OnDisappearing() { _vm.Deactivate(); base.OnDisappearing(); }
    private void UpdateState(Label title, Button edit)
    {
        title.Text = _vm.IsSold ? "Carta vendida" : _vm.IsPublished ? "Anúncio publicado" : _vm.IsReview ? "Revisar anúncio" : "Vender esta carta";
        _editor.IsVisible = !_vm.IsReview && _vm.CanEdit; _review.IsVisible = _vm.IsReview || _vm.IsPublished;
        _editor.IsEnabled = !_vm.IsBusy; edit.IsVisible = _vm.CanEdit; _submit.IsVisible = !_vm.IsPublished;
        _submit.IsEnabled = !_vm.IsBusy; _submit.Text = _vm.IsReview ? "Publicar anúncio" : "Salvar e revisar";
        _submit.Command = _vm.IsReview ? _vm.PublishCommand : _vm.SaveReviewCommand;
    }
    private View Photo(string label, string? type, string source)
    {
        var row = new VerticalStackLayout { Spacing = 4 }; row.Children.Add(MarketplaceTheme.Text(label, 13));
        var image = new Image { HeightRequest = 120, Aspect = Aspect.AspectFit }; image.SetBinding(Image.SourceProperty, source); row.Children.Add(image);
        SemanticProperties.SetDescription(image, $"Foto real da {label.ToLowerInvariant()} da carta.");
        if (type is not null)
        {
            var actions = new HorizontalStackLayout { Spacing = 8 };
            var add = MarketplaceTheme.Action("Fotografar"); add.SetBinding(Button.CommandProperty, nameof(_vm.AddPhotoCommand)); add.CommandParameter = type;
            var remove = MarketplaceTheme.Action("Remover foto"); remove.SetBinding(Button.CommandProperty, nameof(_vm.RemovePhotoCommand)); remove.CommandParameter = type;
            actions.Children.Add(add); actions.Children.Add(remove); row.Children.Add(actions);
        }
        return row;
    }
}
