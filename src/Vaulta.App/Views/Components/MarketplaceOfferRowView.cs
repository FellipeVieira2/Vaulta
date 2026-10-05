using Microsoft.Maui.Controls.Shapes;
using Vaulta.App.Core.Marketplace;
namespace Vaulta.App.Views.Components;
public sealed class MarketplaceOfferRowView : ContentView
{
    public MarketplaceOfferRowView()
    {
        Label Field(string property,bool bold=false,bool secondary=false)
        {var l=MarketplaceTheme.Text(null,14,bold,secondary);l.SetBinding(Label.TextProperty,property);return l;}
        var photo=MarketplaceTheme.Text(null,12,secondary:true);
        BindingContextChanged+=(_,_)=>photo.Text=BindingContext is MarketplaceListingPresentation{ImageDescription:"Foto do vendedor"}
            ?"Fotos da unidade disponíveis":"Fotos da unidade indisponíveis";
        var price=Field(nameof(MarketplaceListingPresentation.Price),true);price.HorizontalOptions=LayoutOptions.End;
        var top=new Grid{ColumnSpacing=8,ColumnDefinitions={new(GridLength.Star),new(GridLength.Auto)}};
        top.Add(Field(nameof(MarketplaceListingPresentation.Metadata),true));top.Add(price,1);
        Content=new Border{StrokeThickness=0,StrokeShape=new RoundRectangle{CornerRadius=8},BackgroundColor=MarketplaceTheme.Surface,
            Padding=12,Margin=new Thickness(0,0,0,12),Content=new VerticalStackLayout{Spacing=8,Children={
                top,Field(nameof(MarketplaceListingPresentation.Seller)),photo}}};
        SetBinding(SemanticProperties.DescriptionProperty,new Binding(nameof(MarketplaceListingPresentation.AccessibleDescription)));
    }
}
