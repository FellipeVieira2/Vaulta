using Microsoft.Maui.Controls.Shapes;
using Vaulta.App.Core.Marketplace;
namespace Vaulta.App.Views.Components;

public sealed class MarketplaceProductCardView : ContentView
{
    public MarketplaceProductCardView()
    {
        var image = new Image { Aspect = Aspect.AspectFit, HeightRequest = 228 };
        image.SetBinding(Image.SourceProperty,nameof(MarketplaceProductPresentation.ArtworkUrl));
        SemanticProperties.SetDescription(image,"Imagem de referência da carta");
        var placeholder = MarketplaceTheme.Text("Imagem indisponível",12,secondary:true);
        placeholder.HorizontalTextAlignment=TextAlignment.Center;placeholder.VerticalTextAlignment=TextAlignment.Center;
        var slot=new Grid {HeightRequest=228,Children={image,placeholder}};
        BindingContextChanged+=(_,_)=>placeholder.IsVisible=BindingContext is not MarketplaceProductPresentation {ArtworkUrl:not null};
        SizeChanged+=(_,_)=>{if(Width>0){slot.HeightRequest=Width*4/3;image.HeightRequest=slot.HeightRequest;}};
        Label Field(string binding,double size,bool bold=false,bool secondary=false)
        {
            var label=MarketplaceTheme.Text(null,size,bold,secondary);label.LineBreakMode=LineBreakMode.WordWrap;
            label.SetBinding(Label.TextProperty,binding);return label;
        }
        var count=Field(nameof(MarketplaceProductPresentation.OfferCount),12);count.TextColor=MarketplaceTheme.Brand;
        var body=new VerticalStackLayout{Spacing=6,Padding=8,Children={
            Field(nameof(MarketplaceProductPresentation.Name),14,true),
            Field(nameof(MarketplaceProductPresentation.Identity),12,secondary:true),
            Field(nameof(MarketplaceProductPresentation.Finish),12,secondary:true),
            MarketplaceTheme.Text("A partir de",12,secondary:true),
            Field(nameof(MarketplaceProductPresentation.LowestPrice),22,true),
            MarketplaceTheme.Text("Referência de mercado",12,secondary:true),
            Field(nameof(MarketplaceProductPresentation.MarketPrice),12,secondary:true),count}};
        Content=new Border {StrokeThickness=0,BackgroundColor=MarketplaceTheme.Surface,StrokeShape=new RoundRectangle{CornerRadius=8},
            Content=new VerticalStackLayout{Spacing=4,Children={slot,body}},Margin=new Thickness(0,0,0,16)};
        SetBinding(SemanticProperties.DescriptionProperty,new Binding(nameof(MarketplaceProductPresentation.AccessibleDescription)));
    }
}
