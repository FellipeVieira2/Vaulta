using System.Globalization;
using Vaulta.App.Core.Marketplace;
using Vaulta.App.Views.Components;
using Vaulta.Marketplace.Contracts;

namespace Vaulta.App.Views;

public sealed class MarketplaceFiltersPage : ContentPage
{
    private readonly IMarketplaceProductClient _client;
    private readonly MarketplaceProductFilters _current;
    private readonly Func<MarketplaceProductFilters,Task> _apply;
    private readonly Picker _set=new(), _language=new(), _variant=new(), _condition=new();
    private readonly Entry _search=new(), _minimum=new(), _maximum=new();
    private readonly CheckBox _photos=new();
    private readonly Label _notice=MarketplaceTheme.Text(null,14,secondary:true);
    private readonly Button _more=MarketplaceTheme.Action("Mais sets");
    private readonly Button _submit=MarketplaceTheme.Action("Ver resultados");
    private readonly Button _clear=MarketplaceTheme.Action("Limpar");
    private readonly List<MarketplaceSetOptionDto> _sets=[];
    private IReadOnlyList<string> _languages=[], _variants=[];
    private Guid? _selectedSet;
    private CancellationTokenSource? _request;
    private bool _busy;
    private bool _optionsLoaded;
    private int _page;
    private static readonly string?[] Conditions=[null,"MINT","NEAR_MINT","LIGHTLY_PLAYED","MODERATELY_PLAYED","HEAVILY_PLAYED","DAMAGED","UNKNOWN"];
    private static readonly CultureInfo Br=CultureInfo.GetCultureInfo("pt-BR");
    public MarketplaceFiltersPage(IMarketplaceProductClient client,MarketplaceProductFilters current,Func<MarketplaceProductFilters,Task> apply)
    {
        _client=client;_current=current;_apply=apply;_selectedSet=current.SetId;
        Title="Filtros";BackgroundColor=MarketplaceTheme.Background;Shell.SetNavBarIsVisible(this,false);Shell.SetTabBarIsVisible(this,false);
        var title=MarketplaceTheme.Text("Filtros",24,true);SemanticProperties.SetHeadingLevel(title,SemanticHeadingLevel.Level1);
        var close=MarketplaceTheme.Action("Fechar");close.Clicked+=async(_,_)=>await Navigation.PopModalAsync();
        var top=new Grid{ColumnDefinitions={new(GridLength.Star),new(GridLength.Auto)},Padding=16};top.Add(title);top.Add(close,1);
        var body=new VerticalStackLayout{Spacing=12,Padding=16};
        body.Children.Add(MarketplaceTheme.Text("Pokémon",14,true));
        void Label(string value)=>body.Children.Add(MarketplaceTheme.Text(value,14,true));
        Label("Set");_search.Placeholder="Buscar set pelo nome";_search.ReturnType=ReturnType.Search;_search.MaxLength=160;
        _search.Completed+=async(_,_)=>await LoadOptions(false);body.Children.Add(_search);
        var search=MarketplaceTheme.Action("Buscar sets");search.Clicked+=async(_,_)=>await LoadOptions(false);body.Children.Add(search);
        _set.Title="Todos os sets";_set.SelectedIndexChanged+=(_,_)=>
        {
            if (_busy) return;
            _selectedSet = _set.SelectedIndex > 0 && _set.SelectedIndex <= _sets.Count
                ? _sets[_set.SelectedIndex-1].Id : null;
        };
        body.Children.Add(_set);_more.IsVisible=false;_more.Clicked+=async(_,_)=>await LoadOptions(true);body.Children.Add(_more);
        Label("Variante / acabamento");_variant.Title="Todos os acabamentos";body.Children.Add(_variant);
        Label("Idioma");_language.Title="Todos os idiomas";body.Children.Add(_language);
        Label("Condição");_condition.ItemsSource=new[]{"Qualquer condição","M · Mint","NM · Quase nova","LP · Levemente jogada","MP · Moderadamente jogada","HP · Muito jogada","DMG · Danificada","Não informada"};
        _condition.SelectedIndex=Math.Max(0,Array.IndexOf(Conditions,current.Condition));body.Children.Add(_condition);
        Label("Preço pedido em reais");_minimum.Placeholder="Mínimo (R$)";_maximum.Placeholder="Máximo (R$)";
        _minimum.Text=current.MinPriceBrl?.ToString("0.##",Br);_maximum.Text=current.MaxPriceBrl?.ToString("0.##",Br);
        body.Children.Add(_minimum);body.Children.Add(_maximum);
        _photos.IsChecked=current.PhotosOnly;var photoRow=new HorizontalStackLayout{Spacing=8,Children={_photos,MarketplaceTheme.Text("Somente anúncios com fotos",14)}};
        body.Children.Add(photoRow);body.Children.Add(_notice);
        foreach(var picker in new[]{_set,_language,_variant,_condition}){picker.TextColor=MarketplaceTheme.Primary;picker.TitleColor=MarketplaceTheme.Secondary;picker.FontFamily=MarketplaceTheme.RegularFont;picker.MinimumHeightRequest=48;}
        foreach(var entry in new[]{_search,_minimum,_maximum}){entry.TextColor=MarketplaceTheme.Primary;entry.PlaceholderColor=MarketplaceTheme.Secondary;entry.FontFamily=MarketplaceTheme.RegularFont;entry.MinimumHeightRequest=48;}
        _minimum.Keyboard=_maximum.Keyboard=Keyboard.Numeric;
        _clear.Clicked+=(_,_)=>{_selectedSet=null;_set.SelectedIndex=0;_variant.SelectedIndex=_language.SelectedIndex=_condition.SelectedIndex=0;_minimum.Text=_maximum.Text="";_photos.IsChecked=false;};
        _submit.IsEnabled=false;_submit.BackgroundColor=MarketplaceTheme.Brand;_submit.TextColor=MarketplaceTheme.Background;_submit.Clicked+=Apply;
        var actions=new Grid{Padding=16,ColumnSpacing=8,ColumnDefinitions={new(GridLength.Star),new(GridLength.Star)}};actions.Add(_clear);actions.Add(_submit,1);
        var layout=new Grid{RowDefinitions={new(GridLength.Auto),new(GridLength.Star),new(GridLength.Auto)}};layout.Add(top);layout.Add(new ScrollView{Content=body},0,1);layout.Add(actions,0,2);Content=layout;
    }
    protected override async void OnAppearing(){base.OnAppearing();await LoadOptions(false);}
    protected override void OnDisappearing(){_request?.Cancel();base.OnDisappearing();}
    private async Task LoadOptions(bool more)
    {
        if(_busy)return;_busy=true;_more.IsEnabled=false;_submit.IsEnabled=false;_clear.IsEnabled=false;_notice.Text="Carregando opções…";
        _set.IsEnabled=_language.IsEnabled=_variant.IsEnabled=false;
        _request?.Cancel();_request?.Dispose();_request=new();var token=_request.Token;
        var language=_optionsLoaded?(_language.SelectedIndex>0?_languages[_language.SelectedIndex-1]:null):_current.Language;
        var variant=_optionsLoaded?(_variant.SelectedIndex>0?_variants[_variant.SelectedIndex-1]:null):_current.VariantCode;
        try
        {
            var result=await _client.GetFilterOptionsAsync(_current.GameCode,_search.Text,more?_page+1:1,token);token.ThrowIfCancellationRequested();
            if(!more)_sets.Clear();_sets.AddRange(result.Sets.Where(x=>_sets.All(s=>s.Id!=x.Id)));
            if(_selectedSet is {} id&&_sets.All(x=>x.Id!=id))_sets.Insert(0,new(id,"Set selecionado"));
            var selected=_selectedSet;_set.ItemsSource=new[]{"Todos os sets"}.Concat(_sets.Select(x=>x.Name)).ToArray();
            _set.SelectedIndex=selected is {} set?_sets.FindIndex(x=>x.Id==set)+1:0;_selectedSet=selected;
            _languages=result.Languages;_variants=result.VariantCodes;
            _language.ItemsSource=new[]{"Todos os idiomas"}.Concat(_languages.Select(LanguageName)).ToArray();_language.SelectedIndex=language is null?0:_languages.ToList().IndexOf(language)+1;
            _variant.ItemsSource=new[]{"Todos os acabamentos"}.Concat(_variants.Select(VariantName)).ToArray();_variant.SelectedIndex=variant is null?0:_variants.ToList().IndexOf(variant)+1;
            _page=result.SetPage;_more.IsVisible=_page*result.SetPageSize<result.SetTotalCount;
            _optionsLoaded=true;
            _notice.Text="Opções do catálogo. Algumas cartas podem estar sem ofertas.";
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception e){_notice.Text=Vaulta.App.Core.Http.ApiErrorTranslator.FromException(e).Message;}
        finally{_busy=false;_more.IsEnabled=true;_clear.IsEnabled=true;_submit.IsEnabled=_optionsLoaded;_set.IsEnabled=_language.IsEnabled=_variant.IsEnabled=true;}
    }
    private async void Apply(object? sender,EventArgs args)
    {
        if(!_submit.IsEnabled)return;
        if(!Price(_minimum.Text,out var min)||!Price(_maximum.Text,out var max)||min>max)
        {_notice.Text="Informe preços válidos, com mínimo menor ou igual ao máximo.";return;}
        _submit.IsEnabled=false;
        try
        {
            var filters=_current with {SetId=_selectedSet,Language=_language.SelectedIndex>0?_languages[_language.SelectedIndex-1]:null,
                VariantCode=_variant.SelectedIndex>0?_variants[_variant.SelectedIndex-1]:null,Condition=Conditions[Math.Max(0,_condition.SelectedIndex)],
                MinPriceBrl=min,MaxPriceBrl=max,PhotosOnly=_photos.IsChecked};
            await _apply(filters);await Navigation.PopModalAsync();
        }
        catch(Exception e){_notice.Text=Vaulta.App.Core.Http.ApiErrorTranslator.FromException(e).Message;}
        finally{_submit.IsEnabled=true;}
    }
    private static bool Price(string? text,out decimal? value)
    {
        value=null;if(string.IsNullOrWhiteSpace(text))return true;
        var culture=text.Contains(',')?Br:CultureInfo.InvariantCulture;
        if(!decimal.TryParse(text,NumberStyles.Number,culture,out var price)||price<0)return false;value=price;return true;
    }
    private static string LanguageName(string code)=>code switch{"en"=>"Inglês","pt" or "pt-br"=>"Português","ja" or "jp"=>"Japonês","es"=>"Espanhol","fr"=>"Francês","de"=>"Alemão","it"=>"Italiano",_=>code};
    private static string VariantName(string code)=>code switch{"normal"=>"Normal","holo"=>"Holo","reverse"=>"Reverse Holo",_=>code};
}
