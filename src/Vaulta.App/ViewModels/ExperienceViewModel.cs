using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Collection;
using Vaulta.App.Core.Http;
using Vaulta.App.Services.Authentication;
using Vaulta.App.State;
using Vaulta.Catalog.Contracts;
using Vaulta.Collection.Contracts;
using Vaulta.Identity.Contracts;

namespace Vaulta.App.ViewModels;

public sealed record ScreenAction(string Title, string? Route, bool IsPrimary = false);
public sealed record ScreenMetric(string Label, string Value, string? Detail = null);
public sealed record ScreenCard(
    string Game,
    string Title,
    string Detail,
    string Price,
    string? Change = null,
    string? ArtworkUrl = null,
    Guid? PrintingId = null,
    Guid? VariantId = null,
    Guid? EntryId = null,
    Guid? ItemId = null);

public sealed record ScreenDefinition(
    string Id,
    string Title,
    string Subtitle,
    string? Eyebrow = null,
    string? Notice = null,
    IReadOnlyList<ScreenMetric>? Metrics = null,
    IReadOnlyList<ScreenCard>? Cards = null,
    IReadOnlyList<string>? Options = null,
    IReadOnlyList<ScreenAction>? Actions = null,
    string? InputHint = null);

public static class ScreenCatalog
{
    // External provider reference for the explicitly labelled UI preview. No Vaulta asset is created.
    public const string CharizardArtwork = "https://assets.tcgdex.net/en/sv/sv03/223/high.webp";
    private static readonly ScreenDefinition[] Definitions =
    [
        new("welcome", "Sua coleção.\nSeu próximo capítulo.", "Identifique, organize e acompanhe o valor das cartas que fazem parte da sua história.", "VAULTA · PREMIUM TCG VAULT", Actions: [new("Começar", "onboarding-1", true), new("Já tenho uma conta", "login")]),
        new("onboarding-1", "Organize sua coleção", "Catalogue cada carta. Saiba exatamente o que você tem e acompanhe tudo em um só lugar.", "01 / 02", Actions: [new("Próximo", "onboarding-2", true), new("Pular", "login")]),
        new("onboarding-2", "Descubra quanto ela vale", "Acompanhe valores, veja tendências e mantenha seu portfólio sempre à vista.", "VALOR DA COLEÇÃO · R$ 24.850,00", Actions: [new("Criar minha conta", "signup", true), new("Entrar", "login")]),
        new("login", "Bem-vindo de volta", "Acesse sua coleção e continue de onde parou.", "ENTRAR NA VAULTA", Actions: [new("Esqueci minha senha", "unsupported"), new("Entrar", "login-submit", true), new("Continuar com Google", "unsupported"), new("Criar conta", "signup")]),
        new("signup", "Sua coleção começa aqui", "Crie sua conta para organizar e acompanhar seus TCGs.", "CRIAR CONTA", Actions: [new("Criar conta", "signup-submit", true), new("Continuar com Google", "unsupported"), new("Já tenho uma conta · Entrar", "login")]),
        new("home", "Olá, colecionador", "Sua coleção, oportunidades e cartas favoritas em um só lugar.", "BEM-VINDO À VAULTA", Notice: "Prévia de interface · os dados de mercado e portfólio ainda são demonstrativos.", Metrics: [new("ESTIMATIVA TOTAL", "Indisponível", "Pricing ainda não integrado"), new("CARTAS", "0", "na coleção")]),
        new("collection", "Minha coleção", "Suas cartas e o valor estimado do seu acervo.", "MEU VAULT", Metrics: [new("CARTAS", "0"), new("VALOR ESTIMADO", "Indisponível")], Actions: [new("Adicionar carta", "catalog", true)]),
        new("empty-collection", "Sua coleção começa aqui", "Escaneie ou busque sua primeira carta para acompanhar seu acervo.", "MEU VAULT", Actions: [new("Escanear carta", "scanner", true), new("Buscar manualmente", "catalog"), new("Adicionar anúncio", "market")]),
        new("catalog", "Encontre sua próxima carta", "Busque cartas, sets e expansões de diferentes TCGs.", "CATÁLOGO", InputHint: "Buscar carta, set ou expansão...", Options: ["Pokémon", "Magic", "Yu-Gi-Oh!", "One Piece"], Actions: [new("Abrir detalhes", "card-detail", true)]),
        new("search-results", "Resultados da busca", "Cartas encontradas no catálogo Vaulta.", "CATÁLOGO", InputHint: "Buscar carta, set ou expansão...", Actions: [new("Ver carta", "card-detail", true)]),
        new("card-detail", "Carregando carta...", "Selecione uma carta no catálogo para ver os detalhes.", "DETALHES DA CARTA", Metrics: [new("PREÇO DE MERCADO", "Indisponível", "Pricing ainda não integrado")], Actions: [new("Adicionar à coleção", "add-to-collection", true), new("Adicionar à wishlist", "wishlist")]),
        new("add-to-collection", "Adicionar à coleção", "Registre condição, quantidade e custo de aquisição.", "NOVO ITEM", Metrics: [new("PREÇO DE MERCADO", "Indisponível", "Pricing ainda não integrado")], Options: ["Mint", "Near Mint", "Lightly Played", "Moderately Played", "Heavily Played", "Damaged"], Actions: [new("Adicionar à coleção", "add-to-collection-submit", true)]),
        new("collection-item", "Detalhes do card", "Charizard ex · Obsidian Flames · 125/197", "SUA COLEÇÃO", Metrics: [new("PAGO INICIAL", "R$ 65,00"), new("PREÇO DE MERCADO", "R$ 89,90"), new("LUCRO ESTIMADO", "+ R$ 24,90")], Actions: [new("Editar anúncio", "sell", true), new("Vender item", "sell"), new("Remover da coleção", "collection")]),
        new("scanner", "Centralize o card", "Enquadre a carta inteira e evite reflexos para obter uma identificação melhor.", "SCANNER", Actions: [new("Identificar carta", "scanner-analyzing", true), new("Buscar manualmente", "catalog")]),
        new("scanner-analyzing", "Identificando carta...", "Analisando imagem e detalhes da carta. Esta é uma prévia do fluxo do scanner.", "SCANNER · DEMONSTRAÇÃO", Actions: [new("Ver resultado de exemplo", "scan-result", true)]),
        new("scan-result", "Pikachu VMAX", "Lost Origin · 029/196 · Full Art · Ultra Rare", "RESULTADO DO SCAN · 98%", Actions: [new("Confirmar seleção", "scan-candidates", true), new("Não é essa carta?", "scan-candidates")]),
        new("scan-candidates", "Selecione o card", "Vários resultados podem corresponder. Escolha a impressão correta.", "VÁRIOS MATCHES", Cards: [new("LOST ORIGIN", "Pikachu VMAX", "029/196 · Full Art", "98%"), new("SWSH PROMO", "Pikachu VMAX", "SWSH286 · Promo", "82%"), new("LOST ORIGIN", "Pikachu V", "120/196 · Standard", "68%")], Actions: [new("Confirmar seleção", "scan-confirmed", true)]),
        new("scan-confirmed", "Carta identificada!", "Pikachu VMAX · Lost Origin foi confirmada na prévia. A adição real depende da integração de coleção.", "SCAN CONCLUÍDO · DEMONSTRAÇÃO", Actions: [new("Ver fluxo de adicionar", "add-to-collection", true), new("Escanear outra", "scanner")]),
        new("portfolio", "Meu Vault", "Acompanhe o valor e a evolução estimada da sua coleção.", "PORTFÓLIO · DEMONSTRAÇÃO", Metrics: [new("TOTAL ESTIMADO", "R$ 12.450,00"), new("CUSTO DE AQUISIÇÃO", "R$ 8.920,00"), new("LUCRO TOTAL", "+ R$ 3.530,00")], Cards: [new("POKÉMON", "Charizard ex", "Mais valorizadas", "R$ 420,00", "+12,4%"), new("MAGIC", "Black Lotus", "Mais valioso", "R$ 1.950,00"), new("YU-GI-OH!", "Blue-Eyes White Dragon", "Acompanhe o mercado", "R$ 310,00", "-2,8%")]),
        new("market", "Mercado Vaulta", "Descubra cartas e oportunidades de colecionadores.", "MERCADO · DEMONSTRAÇÃO", InputHint: "Buscar carta, TCG ou edição...", Options: ["Pokémon", "Magic", "One Piece", "Yu-Gi-Oh!"], Cards: [new("ONE PIECE", "Monkey D. Luffy · OP-05", "Near Mint · Envio nacional", "R$ 950,00"), new("POKÉMON", "Espeon VMAX", "Promo · Near Mint", "R$ 320,00"), new("MAGIC", "Black Lotus", "Collector edition", "R$ 8.500,00")], Actions: [new("Ver anúncio", "listing-detail", true), new("Vender uma carta", "sell")]),
        new("listing-detail", "Detalhes do item", "Pokémon Card 151 · #094/165 · Reverse Holo", "POKÉMON TCG", Metrics: [new("PREÇO", "R$ 82,00"), new("MÉDIA DE MERCADO", "R$ 97,00")], Cards: [new("VENDEDOR", "Thiago TCG", "4,9 · 42 vendas · Belo Horizonte, MG", "envio nacional")], Actions: [new("Comprar agora", null, true), new("Fazer oferta", null)]),
        new("sell", "Vender carta", "Informe condição, preço e fotos reais do item.", "NOVO ANÚNCIO · DEMONSTRAÇÃO", Notice: "O marketplace ainda não está integrado; nenhum anúncio será publicado.", Options: ["Near Mint", "Lightly Played", "Moderately Played", "Heavily Played"], Actions: [new("Publicar anúncio", "unsupported", true)]),
        new("profile", "Lucas Oliveira", "@lucas_tcg · Colecionador desde 2012", "PERFIL DO COLECIONADOR", Metrics: [new("CARTAS", "284"), new("TROCAS", "57"), new("VALOR", "R$ 12,4k")], Actions: [new("Editar perfil", "settings", true), new("Configurações", "settings"), new("Minha coleção", "collection")]),
        new("settings", "Configurações", "Gerencie sua conta e preferências da Vaulta.", "CONTA E SEGURANÇA", Options: ["Conta", "TCGs preferidos", "Segurança", "Notificações", "Privacidade", "Tema: Escuro"], Actions: [new("TCGs preferidos", "preferences-tcg", true), new("Sair da conta", "logout")]),
        new("preferences-tcg", "Seus TCGs", "O que você coleciona? Vamos personalizar sua experiência.", "PREFERÊNCIAS", Options: ["Pokémon", "Magic: The Gathering", "Yu-Gi-Oh!", "One Piece TCG", "Digimon", "Lorcana"], Actions: [new("Salvar preferências", "home", true)]),
        new("wishlist", "Sua wishlist", "Cartas que você quer acompanhar.", "WISHLIST", Actions: [new("Explorar catálogo", "catalog", true)])
    ];

    public static IReadOnlyList<ScreenDefinition> MainTabs { get; } = [Get("home"), Get("collection"), Get("market"), Get("profile")];
    public static IReadOnlyList<ScreenDefinition> All => Definitions;
    public static ScreenDefinition Get(string id) => Definitions.FirstOrDefault(screen => screen.Id == id) ?? Get("home");
}

public partial class ExperienceViewModel : ObservableObject
{
    private readonly IAuthenticationService _authenticationService;
    private readonly SessionState _sessionState;
    private readonly ICatalogClient _catalogClient;
    private readonly ICollectionClient _collectionClient;
    private readonly Dictionary<string, Guid> _variantIdsByLabel = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _searchDebounceCts;
    private AddToCollectionIntent? _addToCollectionIntent;
    private Guid? _loadedPrintingId;
    private bool _collectionLoadedOnce;

    public ExperienceViewModel(
        IAuthenticationService authenticationService,
        SessionState sessionState,
        ICatalogClient catalogClient,
        ICollectionClient collectionClient)
    {
        _authenticationService = authenticationService;
        _sessionState = sessionState;
        _catalogClient = catalogClient;
        _collectionClient = collectionClient;
        SelectedTcgs = ["Pokémon"];
    }

    [ObservableProperty] private string screenId = "home";
    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatusMessage))] private string? statusMessage;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string selectedCondition = "Near Mint";
    [ObservableProperty] private string quantity = "1";
    [ObservableProperty] private string acquisitionCost = string.Empty;
    [ObservableProperty] private string itemNotes = string.Empty;
    [ObservableProperty] private string? selectedVariantLabel;
    [ObservableProperty] private CatalogPrintingDetails? selectedPrinting;
    [ObservableProperty] private CollectionEntryDetailsDto? selectedEntry;
    [ObservableProperty] private CollectibleItemDto? selectedItem;
    [ObservableProperty] private CollectionSummaryDto? collectionSummary;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public ObservableCollection<string> SelectedTcgs { get; } = [];
    public ObservableCollection<string> VariantLabels { get; } = [];
    public ObservableCollection<ScreenCard> CatalogResults { get; } = [];
    public ObservableCollection<ScreenCard> CollectionEntries { get; } = [];
    public Guid? SelectedPrintingId { get; private set; }
    public Guid? SelectedVariantId { get; private set; }
    public Guid? SelectedEntryId { get; private set; }
    public ScreenDefinition Screen => MergeDynamicData(ScreenCatalog.Get(ScreenId));
    public string DisplayGreeting => string.IsNullOrWhiteSpace(_sessionState.User?.DisplayName) ? Screen.Title : $"Olá, {_sessionState.User.DisplayName}";

    public void RefreshGreeting() => OnPropertyChanged(nameof(DisplayGreeting));

    /// <summary>
    /// Called when the user taps a rendered card. Stores the real backend ID carried by that
    /// card (never invented) so the next screen can load real data instead of a mock.
    /// </summary>
    public void SelectCard(string fromScreenId, ScreenCard card)
    {
        switch (fromScreenId)
        {
            case "catalog" or "search-results" when card.PrintingId is { } printingId:
                SelectedPrintingId = printingId;
                break;
            case "collection" when card.EntryId is { } entryId:
                SelectedEntryId = entryId;
                break;
        }
    }

    partial void OnScreenIdChanged(string value)
    {
        OnPropertyChanged(nameof(Screen));
        OnPropertyChanged(nameof(DisplayGreeting));
        StatusMessage = null;
        switch (value)
        {
            case "card-detail" when SelectedPrintingId is { } printingId:
                _ = LoadCardDetailAsync(printingId);
                break;
            case "collection-item" when SelectedEntryId is { } entryId:
                _ = LoadCollectionEntryAsync(entryId);
                break;
            case "collection":
                _ = LoadCollectionAsync();
                break;
            case "home":
                _ = LoadHomeSummaryAsync();
                break;
            case "add-to-collection":
                // Entering the screen is a new voluntary intent; any previous retry key is discarded.
                _addToCollectionIntent = null;
                break;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (ScreenId is not ("catalog" or "search-results")) return;

        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = DebouncedSearchAsync(value, cts.Token);
    }

    private async Task DebouncedSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            await SearchCatalogAsync(query, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task SearchNowAsync()
    {
        _searchDebounceCts?.Cancel();
        await SearchCatalogAsync(SearchText, CancellationToken.None);
    }

    private async Task SearchCatalogAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            CatalogResults.Clear();
            OnPropertyChanged(nameof(Screen));
            return;
        }

        try
        {
            var page = await _catalogClient.SearchAsync(query.Trim(), null, 1, 20, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            CatalogResults.Clear();
            foreach (var item in page.Items)
            {
                CatalogResults.Add(new ScreenCard(
                    FormatGame(item.GameCode),
                    item.CardName,
                    $"{item.SetName} · {item.CollectorNumber}",
                    item.Rarity ?? "—",
                    ArtworkUrl: item.ArtworkUrl,
                    PrintingId: item.PrintingId));
            }
            StatusMessage = CatalogResults.Count == 0 ? "Nenhum resultado encontrado." : null;
            OnPropertyChanged(nameof(Screen));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = ApiErrorMessage(exception);
        }
    }

    private async Task LoadCardDetailAsync(Guid printingId)
    {
        if (_loadedPrintingId == printingId && SelectedPrinting is not null) return;

        IsBusy = true;
        try
        {
            var details = await _catalogClient.GetPrintingAsync(printingId, CancellationToken.None);
            SelectedPrinting = details;
            _loadedPrintingId = printingId;
            VariantLabels.Clear();
            _variantIdsByLabel.Clear();
            foreach (var variant in details.Variants)
            {
                VariantLabels.Add(variant.Name);
                _variantIdsByLabel[variant.Name] = variant.Id;
            }
            SelectedVariantLabel = VariantLabels.FirstOrDefault();
            SelectedVariantId = SelectedVariantLabel is not null ? _variantIdsByLabel[SelectedVariantLabel] : null;
            OnPropertyChanged(nameof(Screen));
        }
        catch (Exception exception)
        {
            StatusMessage = ApiErrorMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCollectionAsync()
    {
        IsBusy = true;
        try
        {
            var page = await _collectionClient.GetCollectionAsync(new CollectionQuery(null, null, null, null, null, 1, 50, "recent"));
            CollectionEntries.Clear();
            foreach (var item in page.Items)
            {
                var condition = item.Conditions.Keys.FirstOrDefault();
                CollectionEntries.Add(new ScreenCard(
                    FormatGame(item.GameCode),
                    item.CardName,
                    $"{item.SetName} · {(condition is null ? item.CollectorNumber : ConditionMapper.ToUiLabel(condition))}",
                    $"{item.Quantity}x",
                    ArtworkUrl: item.ArtworkUrl,
                    EntryId: item.CollectionEntryId));
            }
            try { CollectionSummary = await _collectionClient.GetSummaryAsync(); }
            catch { /* summary is a bonus metric; the list itself already loaded successfully */ }
        }
        catch (Exception exception)
        {
            StatusMessage = ApiErrorMessage(exception);
        }
        finally
        {
            IsBusy = false;
            _collectionLoadedOnce = true;
            OnPropertyChanged(nameof(Screen));
        }
    }

    private async Task LoadCollectionEntryAsync(Guid entryId)
    {
        IsBusy = true;
        try
        {
            var entry = await _collectionClient.GetEntryAsync(entryId, 1, 20);
            SelectedEntry = entry;
            var firstItem = entry.Items.FirstOrDefault();
            SelectedItem = firstItem;
            SelectedCondition = firstItem is not null ? ConditionMapper.ToUiLabel(firstItem.Condition) : SelectedCondition;
            ItemNotes = firstItem?.Notes ?? string.Empty;
            OnPropertyChanged(nameof(Screen));
        }
        catch (Exception exception)
        {
            StatusMessage = ApiErrorMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadHomeSummaryAsync()
    {
        try
        {
            CollectionSummary = await _collectionClient.GetSummaryAsync();
            OnPropertyChanged(nameof(Screen));
        }
        catch
        {
            // The home metric stays in its demonstrative/neutral state when the summary can't load.
        }
    }

    [RelayCommand]
    private async Task AddToCollectionAsync()
    {
        if (IsBusy) return;
        if (SelectedPrintingId is not { } printingId)
        {
            StatusMessage = "Selecione uma carta no catálogo antes de adicionar.";
            return;
        }
        if (!int.TryParse(Quantity, out var quantity) || quantity < 1)
        {
            StatusMessage = "Informe uma quantidade válida.";
            return;
        }

        _addToCollectionIntent ??= new AddToCollectionIntent();
        var conditionCode = ConditionMapper.ToCanonicalCode(SelectedCondition);
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var request = new AddCollectibleItemsRequest(
                printingId,
                SelectedVariantId,
                quantity,
                conditionCode,
                AcquisitionPrice: null,
                AcquisitionDate: null,
                Notes: string.IsNullOrWhiteSpace(ItemNotes) ? null : ItemNotes);
            await _collectionClient.AddItemsAsync(request, _addToCollectionIntent.IdempotencyKey);
            _addToCollectionIntent = null; // this voluntary action succeeded; the next add needs a new key
            CollectionSummary = null;
            await Shell.Current.GoToAsync("//main/collection/collection-page");
        }
        catch (Exception exception)
        {
            // Deliberately keep _addToCollectionIntent so an automatic retry reuses the same key.
            StatusMessage = ApiErrorMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateCollectionItemAsync()
    {
        if (IsBusy || SelectedItem is null) return;

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var conditionCode = ConditionMapper.ToCanonicalCode(SelectedCondition);
            var request = new UpdateCollectibleItemRequest(
                conditionCode,
                SelectedItem.AcquisitionPrice,
                SelectedItem.AcquisitionDate,
                string.IsNullOrWhiteSpace(ItemNotes) ? null : ItemNotes,
                SelectedItem.Version);
            var newVersion = await _collectionClient.UpdateItemAsync(SelectedItem.Id, request);
            SelectedItem = SelectedItem with { Condition = conditionCode, Notes = request.Notes, Version = newVersion };
            StatusMessage = "Alterações salvas.";
        }
        catch (ApiException exception) when (exception.StatusCode == 409)
        {
            StatusMessage = exception.Message;
            if (SelectedEntry is not null)
                await LoadCollectionEntryAsync(SelectedEntry.CollectionEntryId);
        }
        catch (Exception exception)
        {
            StatusMessage = ApiErrorMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveCollectionItemAsync()
    {
        if (IsBusy || SelectedItem is null) return;

        IsBusy = true;
        try
        {
            await _collectionClient.RemoveItemAsync(SelectedItem.Id, SelectedItem.Version);
            CollectionSummary = null;
            await Shell.Current.GoToAsync("//main/collection/collection-page");
        }
        catch (Exception exception)
        {
            StatusMessage = ApiErrorMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private ScreenDefinition MergeDynamicData(ScreenDefinition baseScreen) => baseScreen.Id switch
    {
        "catalog" or "search-results" => CatalogResults.Count > 0 ? baseScreen with { Cards = CatalogResults.ToArray() } : baseScreen,
        "card-detail" => MergeCardDetail(baseScreen),
        "collection" => MergeCollection(baseScreen),
        "collection-item" => MergeCollectionItem(baseScreen),
        "home" => MergeHome(baseScreen),
        _ => baseScreen
    };

    private ScreenDefinition MergeCardDetail(ScreenDefinition baseScreen)
    {
        if (SelectedPrinting is null) return baseScreen;
        var printing = SelectedPrinting;
        return baseScreen with
        {
            Title = printing.CardName,
            Subtitle = $"{printing.SetName} · {printing.CollectorNumber} · {FormatGame(printing.GameCode)}",
            Options = VariantLabels.Count > 0 ? VariantLabels.ToArray() : null
        };
    }

    private ScreenDefinition MergeCollection(ScreenDefinition baseScreen)
    {
        if (IsBusy && CollectionEntries.Count == 0) return baseScreen with { Cards = null, Metrics = null };
        if (!_collectionLoadedOnce) return baseScreen with { Cards = null };

        if (CollectionEntries.Count == 0)
        {
            var empty = ScreenCatalog.Get("empty-collection");
            return baseScreen with { Title = empty.Title, Subtitle = empty.Subtitle, Cards = null, Metrics = null, Actions = empty.Actions };
        }

        var metrics = (baseScreen.Metrics ?? []).Select(metric => metric.Label == "CARTAS" && CollectionSummary is not null
            ? metric with { Value = CollectionSummary.TotalItems.ToString() }
            : metric).ToArray();
        return baseScreen with { Cards = CollectionEntries.ToArray(), Metrics = metrics };
    }

    private ScreenDefinition MergeCollectionItem(ScreenDefinition baseScreen)
    {
        if (SelectedEntry is null) return baseScreen;
        var entry = SelectedEntry;
        var conditionLabel = SelectedItem is not null ? ConditionMapper.ToUiLabel(SelectedItem.Condition) : "—";
        return baseScreen with
        {
            Title = entry.CardName,
            Subtitle = $"{entry.SetName} · {entry.CollectorNumber} · {conditionLabel}",
            Metrics = [new ScreenMetric("QUANTIDADE", entry.Quantity.ToString()), new ScreenMetric("PREÇO DE MERCADO", "Indisponível", "Pricing ainda não integrado")],
            Options = ConditionMapper.UiLabels.ToArray(),
            Actions = [new ScreenAction("Salvar alterações", "collection-item-update", true), new ScreenAction("Remover da coleção", "collection-item-remove")]
        };
    }

    private ScreenDefinition MergeHome(ScreenDefinition baseScreen)
    {
        if (CollectionSummary is null) return baseScreen;
        var metrics = (baseScreen.Metrics ?? []).Select(metric => metric.Label == "CARTAS"
            ? metric with { Value = CollectionSummary.TotalItems.ToString() }
            : metric).ToArray();
        return baseScreen with { Metrics = metrics };
    }

    private static string FormatGame(string gameCode) => gameCode.ToUpperInvariant();

    private static string ApiErrorMessage(Exception exception) => Vaulta.App.Core.Http.ApiErrorTranslator.FromException(exception).Message;

    [RelayCommand]
    private void SelectCondition(string? condition)
    {
        if (!string.IsNullOrWhiteSpace(condition))
            SelectedCondition = condition;
    }

    [RelayCommand]
    private void SelectVariant(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) || !_variantIdsByLabel.TryGetValue(label, out var id)) return;
        SelectedVariantLabel = label;
        SelectedVariantId = id;
    }

    [RelayCommand]
    private async Task NavigateAsync(string? route)
    {
        if (IsBusy) return;
        if (ScreenId.StartsWith("onboarding-", StringComparison.Ordinal) && (route is "login" or "signup"))
            AppShell.CompleteOnboarding();
        if (string.IsNullOrWhiteSpace(route) || route == "unsupported")
        {
            StatusMessage = "Esta ação estará disponível quando o serviço correspondente for integrado.";
            return;
        }

        if (route == "logout")
        {
            await LogoutAsync();
            return;
        }

        if (route == "login-submit")
        {
            await LoginAsync();
            return;
        }

        if (route == "signup-submit")
        {
            await RegisterAsync();
            return;
        }

        if (route == "add-to-collection-submit")
        {
            await AddToCollectionAsync();
            return;
        }

        if (route == "collection-item-update")
        {
            await UpdateCollectionItemAsync();
            return;
        }

        if (route == "collection-item-remove")
        {
            await RemoveCollectionItemAsync();
            return;
        }

        if (route is "home" or "collection" or "market" or "profile")
        {
            await Shell.Current.GoToAsync($"//main/{route}/{route}-page");
            return;
        }

        if (route is "welcome" or "login")
        {
            await Shell.Current.GoToAsync("experience?screen=login");
            return;
        }

        await Shell.Current.GoToAsync($"experience?screen={Uri.EscapeDataString(route)}");
    }

    [RelayCommand]
    private void ToggleTcg(string? tcg)
    {
        if (string.IsNullOrWhiteSpace(tcg)) return;
        if (!SelectedTcgs.Remove(tcg)) SelectedTcgs.Add(tcg);
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        await RunAuthenticationAsync(async () =>
        {
            await _authenticationService.LoginAsync(new LoginRequest(Email.Trim(), Password));
            await Shell.Current.GoToAsync("//main/home/home-page");
        });
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(Username))
        {
            StatusMessage = "Informe seu nome e nome de usuário.";
            return;
        }
        if (string.IsNullOrEmpty(Password) || Password.Length is < 12 or > 128 || !Password.Any(char.IsUpper) || !Password.Any(char.IsLower) ||
            !Password.Any(char.IsDigit) || Password.All(char.IsLetterOrDigit))
        {
            StatusMessage = "Use uma senha de 12 a 128 caracteres, com maiúscula, minúscula, número e símbolo.";
            return;
        }
        await RunAuthenticationAsync(async () =>
        {
            await _authenticationService.RegisterAsync(new RegisterRequest(Email.Trim(), Password, Username.Trim(), DisplayName.Trim()));
            await _authenticationService.LoginAsync(new LoginRequest(Email.Trim(), Password));
            await Shell.Current.GoToAsync("//main/home/home-page");
        });
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        IsBusy = true;
        try
        {
            await _authenticationService.LogoutAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = AuthenticationError(exception);
        }
        finally
        {
            IsBusy = false;
            await ((AppShell)Shell.Current).ShowLoginAsync();
        }
    }

    private async Task RunAuthenticationAsync(Func<Task> action)
    {
        if (IsBusy) return;
        StatusMessage = null;
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            StatusMessage = "Informe e-mail e senha para continuar.";
            return;
        }

        IsBusy = true;
        try
        {
            await action();
            Password = string.Empty;
        }
        catch (Exception exception)
        {
            StatusMessage = AuthenticationError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }
    private static string AuthenticationError(Exception error) => error switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } => "E-mail ou senha incorretos. Confira os dados e tente novamente.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Conflict } => "Este e-mail ou nome de usuário já está em uso.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.BadRequest } => "Confira os dados informados e os requisitos da senha.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => "Muitas tentativas. Aguarde um pouco antes de tentar novamente.",
        HttpRequestException or TaskCanceledException => "Não foi possível conectar. Verifique sua conexão e tente novamente.",
        _ => "Não foi possível concluir agora. Tente novamente em instantes."
    };

}
