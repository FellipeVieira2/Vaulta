using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vaulta.App.Core.Address;
using Vaulta.App.Core.Assets;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Collection;
using Vaulta.App.Core.Http;
using Vaulta.App.Core.Marketplace;
using Vaulta.App.Core.Orders;
using Vaulta.App.Core.Payments;
using Vaulta.Assets.Contracts;
using Vaulta.App.Services.Api;
using Vaulta.App.Services.Authentication;
using Vaulta.App.Services.Camera;
using Vaulta.App.State;
using Vaulta.Catalog.Contracts;
using Vaulta.Collection.Contracts;
using Vaulta.Identity.Contracts;
using Vaulta.Marketplace.Contracts;
using Vaulta.Orders.Contracts;
using Vaulta.Payments.Contracts;

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
        new("home", "Olá, colecionador", "Sua coleção, oportunidades e cartas favoritas em um só lugar.", "BEM-VINDO À VAULTA", Metrics: [new("ESTIMATIVA TOTAL", "Indisponível"), new("CARTAS", "0", "na coleção")], Actions: [new("Escanear carta", "scanner", true), new("Buscar carta", "catalog"), new("Ver valor do acervo", "portfolio")]),
        new("collection", "Minha coleção", "Suas cartas e o valor estimado do seu acervo.", "MEU VAULT", Metrics: [new("CARTAS", "0"), new("VALOR ESTIMADO", "Indisponível")], Actions: [new("Escanear carta", "scanner", true), new("Adicionar carta", "catalog"), new("Ver valor do acervo", "portfolio"), new("Atualizar valores", "collection-valuation-refresh")]),
        new("empty-collection", "Sua coleção começa aqui", "Escaneie ou busque sua primeira carta para acompanhar seu acervo.", "MEU VAULT", Actions: [new("Escanear carta", "scanner", true), new("Buscar manualmente", "catalog"), new("Adicionar anúncio", "market")]),
        new("catalog", "Encontre sua próxima carta", "Busque cartas, sets e expansões de diferentes TCGs.", "CATÁLOGO", InputHint: "Buscar carta, set ou expansão...", Options: ["Pokémon", "Magic", "Yu-Gi-Oh!", "One Piece"], Actions: [new("Abrir detalhes", "card-detail", true)]),
        new("search-results", "Resultados da busca", "Cartas encontradas no catálogo Vaulta.", "CATÁLOGO", InputHint: "Buscar carta, set ou expansão...", Actions: [new("Ver carta", "card-detail", true)]),
        new("card-detail", "Carregando carta...", "Selecione uma carta no catálogo para ver os detalhes.", "DETALHES DA CARTA", Actions: [new("Adicionar à coleção", "add-to-collection", true), new("Adicionar à wishlist", "wishlist")]),
        new("add-to-collection", "Adicionar à coleção", "Registre condição, quantidade e custo de aquisição.", "NOVO ITEM", Options: ["Mint", "Near Mint", "Lightly Played", "Moderately Played", "Heavily Played", "Damaged"], Actions: [new("Adicionar à coleção", "add-to-collection-submit", true)]),
        new("collection-item", "Detalhes da carta", "Carregando a unidade selecionada.", "SUA COLEÇÃO"),
        new("scanner", "Centralize o card", "Enquadre a carta inteira e evite reflexos para obter uma identificação melhor.", "SCANNER", Actions: [new("Buscar manualmente", "catalog")]),
        new("scanner-card-detail", "Informações da carta", "Confira a edição, a variante e os valores antes de adicionar.", "CARTA IDENTIFICADA", Actions: [new("Adicionar à coleção", "add-to-collection", true)]),
        new("scanner-analyzing", "Identificando carta...", "Analisando imagem e detalhes da carta. Esta é uma prévia do fluxo do scanner.", "SCANNER · DEMONSTRAÇÃO", Actions: [new("Ver resultado de exemplo", "scan-result", true)]),
        new("scan-result", "Pikachu VMAX", "Lost Origin · 029/196 · Full Art · Ultra Rare", "RESULTADO DO SCAN · 98%", Actions: [new("Confirmar seleção", "scan-candidates", true), new("Não é essa carta?", "scan-candidates")]),
        new("scan-candidates", "Selecione o card", "Vários resultados podem corresponder. Escolha a impressão correta.", "VÁRIOS MATCHES", Cards: [new("LOST ORIGIN", "Pikachu VMAX", "029/196 · Full Art", "98%"), new("SWSH PROMO", "Pikachu VMAX", "SWSH286 · Promo", "82%"), new("LOST ORIGIN", "Pikachu V", "120/196 · Standard", "68%")], Actions: [new("Confirmar seleção", "scan-confirmed", true)]),
        new("scan-confirmed", "Carta identificada!", "Pikachu VMAX · Lost Origin foi confirmada na prévia. A adição real depende da integração de coleção.", "SCAN CONCLUÍDO · DEMONSTRAÇÃO", Actions: [new("Ver fluxo de adicionar", "add-to-collection", true), new("Escanear outra", "scanner")]),
        new("portfolio", "Meu Vault", "O valor estimado das suas cartas, em reais.", "VALOR DO ACERVO", Actions: [new("Atualizar valores", "collection-valuation-refresh", true), new("Minha coleção", "collection")]),
        new("market", "Mercado Vaulta", "Descubra cartas e oportunidades de colecionadores.", "MERCADO", Actions: [new("Vender uma carta", "sell")]),
        new("listing-detail", "Detalhes do anúncio", "Carregando os dados do anúncio selecionado.", "MERCADO"),
        new("checkout", "Endereço de entrega", "Informe onde devemos entregar seu pedido.", "CHECKOUT", Actions: [new("Confirmar e pagar", null, true)]),
        new("payouts", "Meus repasses", "Você recebe por Pix após o comprador confirmar o recebimento. Os 8% da Vaulta e as tarifas do Asaas são descontados do vendedor.", "VENDAS E REPASSES"),
        new("orders", "Meus pedidos", "Acompanhe compras e vendas. Confirme o recebimento quando a carta chegar.", "PEDIDOS"),
        new("sell", "Vender carta", "Informe condição, preço e fotos reais do item.", "NOVO ANÚNCIO · DEMONSTRAÇÃO", Notice: "O marketplace ainda não está integrado; nenhum anúncio será publicado.", Options: ["Near Mint", "Lightly Played", "Moderately Played", "Heavily Played"], Actions: [new("Publicar anúncio", "unsupported", true)]),
        new("profile", "Meu perfil", "Seus dados, preferências e vendas.", "PERFIL DO COLECIONADOR", Actions: [new("Editar perfil", "settings", true), new("Configurações", "settings"), new("Minha coleção", "collection"), new("Meus pedidos", "orders"), new("Meus repasses", "payouts")]),
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
    private readonly IVaultaApiClient _apiClient;
    private readonly ICameraService _cameraService;
    private readonly IScannerClient _scannerClient;
    private readonly IMarketplaceClient _marketplaceClient;
    private readonly IOrdersClient _ordersClient;
    private readonly IPaymentsClient _paymentsClient;
    private readonly IPayoutsClient _payoutsClient;
    private readonly IViaCepClient _viaCepClient;
    private readonly IPriceHistoryProvider _priceHistoryProvider;
    private readonly ITokenStore _tokenStore;
    private readonly IAssetClient _assetClient;
    private readonly Dictionary<string, Guid> _variantIdsByLabel = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Guid> _sellPhotoAssetIds = [];
    private CancellationTokenSource? _searchDebounceCts;
    private CancellationTokenSource? _cepDebounceCts;
    private AddToCollectionIntent? _addToCollectionIntent;
    private Guid? _loadedPrintingId;
    private bool _collectionLoadedOnce;
    private MyProfileDto? _currentProfile;
    private OrderPageDto? _userOrders;
    private int _payoutPage = 1;
    private int _orderPage = 1;
    private Guid? _privateDataOwner;
    private CancellationTokenSource? _collectionReadCts;
    private CancellationTokenSource? _collectionValueCts;
    private CancellationTokenSource? _entryReadCts;
    private CancellationTokenSource? _entryValueCts;
    private string? _collectionValueError;
    private string? _entryValueError;
    [ObservableProperty] private CollectionValuationDto? collectionValuation;
    [ObservableProperty] private CollectionValuationDto? entryValuation;
    public CollectionValuationDto? VisibleValuation => ScreenId == "collection-item" ? EntryValuation : CollectionValuation;

    public ExperienceViewModel(
        IAuthenticationService authenticationService,
        SessionState sessionState,
        ICatalogClient catalogClient,
        ICollectionClient collectionClient,
        IVaultaApiClient apiClient,
        ICameraService cameraService,
        IScannerClient scannerClient,
        IMarketplaceClient marketplaceClient,
        IOrdersClient ordersClient,
        IPaymentsClient paymentsClient,
        IPayoutsClient payoutsClient,
        IViaCepClient viaCepClient,
        IPriceHistoryProvider priceHistoryProvider,
        ITokenStore tokenStore,
        IAssetClient assetClient)
    {
        _authenticationService = authenticationService;
        _sessionState = sessionState;
        _catalogClient = catalogClient;
        _collectionClient = collectionClient;
        _apiClient = apiClient;
        _cameraService = cameraService;
        _scannerClient = scannerClient;
        _marketplaceClient = marketplaceClient;
        _ordersClient = ordersClient;
        _paymentsClient = paymentsClient;
        _payoutsClient = payoutsClient;
        _viaCepClient = viaCepClient;
        _priceHistoryProvider = priceHistoryProvider;
        _tokenStore = tokenStore;
        _assetClient = assetClient;
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
    [ObservableProperty] private CardScanResultDto? scanResult;
    [ObservableProperty] private ScannerCardDetailsDto? scannerCardDetails;
    [ObservableProperty] private byte[]? capturedImageBytes;
    [ObservableProperty] private ListingPageDto? marketplaceListings;
    [ObservableProperty] private OrderDto? currentOrder;
    [ObservableProperty] private PaymentDto? currentPayment;
    [ObservableProperty] private Guid? selectedListingId;
    [ObservableProperty] private ListingDto? selectedListing;
    private CatalogPrintingDetails? _listingPrinting;
    partial void OnSelectedListingChanged(ListingDto? value) => OnPropertyChanged(nameof(Screen));
    [ObservableProperty] private IReadOnlyList<Vaulta.App.Views.Components.ChartPoint>? filterChartData;
    [ObservableProperty] private string shippingStreet = string.Empty;
    [ObservableProperty] private string shippingNumber = string.Empty;
    [ObservableProperty] private string shippingComplement = string.Empty;
    [ObservableProperty] private string shippingNeighborhood = string.Empty;
    [ObservableProperty] private string shippingCity = string.Empty;
    [ObservableProperty] private string shippingState = string.Empty;
    [ObservableProperty] private string shippingZipCode = string.Empty;
    [ObservableProperty] private string shippingRecipient = string.Empty;
    [ObservableProperty] private string billingLegalName = string.Empty;
    [ObservableProperty] private string billingDocument = string.Empty;
    [ObservableProperty] private bool isAddressLoading;
    [ObservableProperty] private string pixKey = string.Empty;
    [ObservableProperty] private string pixKeyType = "CPF";
    [ObservableProperty] private PixDestinationDto? pixDestination;
    [ObservableProperty] private SellerPayoutPageDto? sellerPayouts;

    partial void OnShippingZipCodeChanged(string value)
    {
        _cepDebounceCts?.Cancel();
        _cepDebounceCts = new CancellationTokenSource();
        _ = LookupAddressAsync(value, _cepDebounceCts.Token);
    }

    private async Task LookupAddressAsync(string cep, CancellationToken cancellationToken)
    {
        var cleanCep = new string(cep.Where(char.IsDigit).ToArray());
        if (cleanCep.Length != 8) return;

        try
        {
            await Task.Delay(300, cancellationToken);
            IsAddressLoading = true;
            var result = await _viaCepClient.GetByCepAsync(cleanCep, cancellationToken);
            if (result is not null)
            {
                ShippingStreet = result.Logradouro ?? string.Empty;
                ShippingNeighborhood = result.Bairro ?? string.Empty;
                ShippingCity = result.Localidade ?? string.Empty;
                ShippingState = result.Uf ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Silently ignore lookup failures; user can still type manually
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                IsAddressLoading = false;
        }
    }
    [RelayCommand]
    private async Task RegisterPixAsync(CancellationToken cancellationToken)
    {
        var userId = _sessionState.User?.Id;
        if (string.IsNullOrWhiteSpace(PixKey))
        {
            StatusMessage = "Informe sua chave Pix.";
            return;
        }
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var destination = await _payoutsClient.RegisterDestinationAsync(new(PixKey.Trim(), PixKeyType), cancellationToken);
            if (userId != _sessionState.User?.Id) return;
            PixDestination = destination;
            PixKey = string.Empty;
            StatusMessage = "Chave consultada. A titularidade será verificada antes dos repasses.";
            OnPropertyChanged(nameof(Screen));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = ApiErrorMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadPayoutsAsync(CancellationToken cancellationToken)
    {
        var userId = _sessionState.User?.Id;
        if (userId is null) return;
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var destination = await _payoutsClient.GetDestinationAsync(cancellationToken);
            var payouts = await _payoutsClient.GetPayoutsAsync(page: _payoutPage, ct: cancellationToken);
            if (userId != _sessionState.User?.Id) return;
            PixDestination = destination;
            SellerPayouts = payouts;
            OnPropertyChanged(nameof(Screen));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = ApiErrorMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnCurrentPaymentChanged(PaymentDto? value) => OnPropertyChanged(nameof(Screen));

    public void ContinueOrderPayment(OrderDto order)
    {
        if (!IsBuyer(order) || order.Status != "pending") return;
        CurrentOrder = order; CurrentPayment = null; SelectedListingId = order.ListingId;
        ShippingStreet = order.Shipping.Street ?? string.Empty;
        ShippingNumber = order.Shipping.Number ?? string.Empty;
        ShippingComplement = order.Shipping.Complement ?? string.Empty;
        ShippingNeighborhood = order.Shipping.Neighborhood ?? string.Empty;
        ShippingCity = order.Shipping.City ?? string.Empty;
        ShippingState = order.Shipping.State ?? string.Empty;
        ShippingZipCode = order.Shipping.ZipCode ?? string.Empty;
        ShippingRecipient = order.Shipping.Recipient ?? string.Empty;
        ScreenId = "checkout";
    }

    [RelayCommand]
    private async Task RefreshPaymentAsync(CancellationToken ct)
    {
        if (IsBusy) return;
        if (CurrentOrder is not { } order || !IsBuyer(order)) return;
        var userId = _sessionState.User?.Id;
        IsBusy = true;
        try
        {
            var payment = await _paymentsClient.GetOrderPaymentAsync(order.Id, ct);
            if (userId != _sessionState.User?.Id) return;
            if (payment?.Status is "pending" or "overdue")
            {
                payment = await _paymentsClient.InitiatePaymentAsync(new(order.Id, payment.BillingType, null), ct);
                if (userId != _sessionState.User?.Id) return;
            }
            CurrentPayment = payment;
            StatusMessage = payment?.Status switch
            {
                "confirmed" => "Pagamento confirmado. Aguarde o envio do vendedor.",
                "refunded" => "Reembolso confirmado.",
                _ => "Aguardando confirmação do pagamento."
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { StatusMessage = ApiErrorMessage(ex); }
        finally { IsBusy = false; }
    }
    [RelayCommand]
    private async Task ChangePayoutPageAsync(int page, CancellationToken cancellationToken)
    {
        _payoutPage = Math.Max(1, page);
        await LoadPayoutsAsync(cancellationToken);
    }

    public IReadOnlyList<OrderDto> UserOrders => _userOrders?.Items ?? [];
    public OrderPageDto? UserOrdersPage => _userOrders;
    public bool IsBuyer(OrderDto order) => order.BuyerId == _sessionState.User?.Id;
    public bool IsSeller(OrderDto order) => order.SellerId == _sessionState.User?.Id;

    [RelayCommand]
    private async Task ChangeOrderPageAsync(int page)
    {
        _orderPage = Math.Max(1, page);
        _userOrders = null;
        await LoadOrdersAsync();
    }

    [RelayCommand]
    private async Task ConfirmReceiptAsync(Guid orderId, CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            await _ordersClient.ConfirmReceiptAsync(orderId, cancellationToken);
            _userOrders = null;
            await LoadOrdersAsync();
            StatusMessage = "Recebimento confirmado. O repasse ao vendedor será processado.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { StatusMessage = ApiErrorMessage(ex); }
        finally { IsBusy = false; }
    }

    public async Task MarkShipmentAsync(Guid orderId, string trackingCode)
    {
        if (string.IsNullOrWhiteSpace(trackingCode)) { StatusMessage = "Informe o código de rastreio."; return; }
        IsBusy = true;
        StatusMessage = null;
        try
        {
            await _ordersClient.MarkShippedAsync(orderId, trackingCode.Trim());
            _userOrders = null;
            await LoadOrdersAsync();
            StatusMessage = "Envio registrado.";
        }
        catch (Exception ex) { StatusMessage = ApiErrorMessage(ex); }
        finally { IsBusy = false; }
    }

    public async Task CancelOrderAsync(Guid orderId, string reason)
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            await _ordersClient.CancelOrderAsync(orderId, reason);
            _userOrders = null;
            await LoadOrdersAsync();
            StatusMessage = "Solicitação registrada. Pedidos pagos aguardam a confirmação do reembolso integral.";
        }
        catch (Exception ex) { StatusMessage = ApiErrorMessage(ex); }
        finally { IsBusy = false; }
    }

    public async Task<OrderRefundDto?> GetRefundAsync(Guid orderId)
    {
        try
        {
            var refund = await _ordersClient.GetRefundAsync(orderId);
            StatusMessage = refund?.Status switch
            {
                "DONE" => "Reembolso integral confirmado.",
                "RECONCILIATION_REQUIRED" => "Reembolso em análise. Procure o atendimento.",
                _ => "Reembolso solicitado. Aguardando a confirmação do Asaas."
            };
            return refund;
        }
        catch (Exception ex) { StatusMessage = ApiErrorMessage(ex); return null; }
    }

    public static string PayoutStatusText(string status) => status switch
    {
        "WAITING_RECEIPT" => "Aguardando recebimento pelo comprador",
        "WAITING_SETTLEMENT" => "Aguardando liquidação do pagamento",
        "WAITING_DESTINATION" => "Aguardando chave Pix verificada",
        "READY" => "Aguardando repasse",
        "SUBMITTING" or "PROCESSING" => "Repasse em processamento",
        "DONE" => "Repasse concluído",
        "FAILED" => "Repasse não concluído · entre em contato com o suporte",
        "INSUFFICIENT_NET" or "BLOCKED" or "RECONCILIATION_REQUIRED" => "Repasse em análise",
        _ => "Verificando repasse"
    };
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasScanCandidates => ScanResult?.Candidates is { Count: > 0 };
    public ObservableCollection<string> SelectedTcgs { get; } = [];
    public ObservableCollection<string> VariantLabels { get; } = [];
    public ObservableCollection<ScreenCard> CatalogResults { get; } = [];
    public ObservableCollection<ScreenCard> CollectionEntries { get; } = [];
    public Guid? SelectedPrintingId { get; private set; }
    public Guid? SelectedVariantId { get; private set; }
    public Guid? SelectedEntryId { get; private set; }
    public ScreenDefinition Screen => MergeDynamicData(ScreenCatalog.Get(ScreenId));
    public string DisplayGreeting => string.IsNullOrWhiteSpace(_sessionState.User?.DisplayName) ? Screen.Title : $"Olá, {_sessionState.User.DisplayName}";

    public void RefreshGreeting()
    {
        var changedOwner = EnsurePrivateDataOwner();
        // Appearing after session import must reload even when the tab's ScreenId did not change.
        if (ScreenId == "collection") _ = LoadCollectionAsync();
        else if (ScreenId == "home") _ = LoadHomeSummaryAsync();
        else if (ScreenId == "portfolio") _ = LoadValuationAsync();
        if (changedOwner)
        {
            if (ScreenId == "profile") _ = LoadProfileAsync();
            if (ScreenId == "orders") _ = LoadOrdersAsync();
            if (ScreenId == "payouts") _ = LoadPayoutsAsync(CancellationToken.None);
            OnPropertyChanged(nameof(Screen));
        }
        OnPropertyChanged(nameof(DisplayGreeting));
    }

    public void SelectPrinting(Guid printingId) => SelectedPrintingId = printingId;

    public IReadOnlyList<Vaulta.App.Views.Components.ChartPoint> GetPortfolioChartData()
    {
        var history = _priceHistoryProvider.GetPortfolioHistory(days: 7);
        return history.Select(p => new Vaulta.App.Views.Components.ChartPoint(p.Date, p.Value, p.Label)).ToList();
    }

    public IReadOnlyList<Vaulta.App.Views.Components.ChartPoint> GetCardChartData(Guid printingId)
    {
        var history = _priceHistoryProvider.GetCardPriceHistory(printingId, days: 30);
        return history.Select(p => new Vaulta.App.Views.Components.ChartPoint(p.Date, p.Value, p.Label)).ToList();
    }

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
            case "market" when card.ItemId is { } listingId:
                SelectedListingId = listingId;
                break;
        }
    }

    partial void OnScreenIdChanged(string value)
    {
        EnsurePrivateDataOwner();
        OnPropertyChanged(nameof(Screen));
        OnPropertyChanged(nameof(DisplayGreeting));
        StatusMessage = null;
        switch (value)
        {
            case "market":
                _ = LoadMarketplaceListingsAsync(CancellationToken.None);
                break;
            case "listing-detail" when SelectedListingId is { } listingId:
                _ = LoadListingDetailAsync(listingId);
                break;
            case "card-detail" or "scanner-card-detail" or "add-to-collection" when SelectedPrintingId is { } printingId:
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
            case "portfolio":
                _ = LoadValuationAsync();
                break;
            case "profile":
                _ = LoadProfileAsync();
                break;
            case "orders":
                _ = LoadOrdersAsync();
                break;
            case "payouts":
                _ = LoadPayoutsAsync(CancellationToken.None);
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

    [RelayCommand]
    private async Task ScanCardAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var imageData = await _cameraService.CapturePhotoAsync(cancellationToken);
            if (imageData is null || imageData.Length == 0)
            {
                StatusMessage = "Captura cancelada ou indisponível.";
                return;
            }

            CapturedImageBytes = imageData;
            ScanResult = null;
            OnPropertyChanged(nameof(Screen));
            const string gameCode = "pokemon";
            var result = await _scannerClient.ScanCardAsync(imageData, gameCode, cancellationToken);
            ScanResult = result;

            if (result.Candidates.Count == 0)
            {
                StatusMessage = "Nenhuma carta identificada. Tente buscar pelo nome.";
            }
            else
            {
                StatusMessage = $"{result.Candidates.Count} resultado(s) encontrado(s).";
            }

            OnPropertyChanged(nameof(Screen));
        }
        catch (OperationCanceledException)
        {
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
    private async Task AddScannedCardToCollectionAsync(CardScanCandidateDto? candidate, CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        if (candidate is null || candidate.PrintingId == Guid.Empty)
        {
            StatusMessage = "Esta carta ainda não está disponível no catálogo. Tente buscar manualmente.";
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        await Shell.Current.GoToAsync($"experience?screen=scanner-card-detail&printingId={candidate.PrintingId}");
    }
    [RelayCommand]
    private async Task LoadMarketplaceListingsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            MarketplaceListings = await _marketplaceClient.ListActiveListingsAsync(page: 1, pageSize: 20, sort: "newest", cancellationToken: cancellationToken);
            if (MarketplaceListings.Items.Count == 0)
                StatusMessage = "Nenhum anúncio ativo no momento.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = ApiErrorMessage(exception);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(Screen));
        }
    }

    private async Task LoadListingDetailAsync(Guid listingId)
    {
        SelectedListing = null; _listingPrinting = null;
        IsBusy = true;
        try
        {
            var listing = await _marketplaceClient.GetListingAsync(listingId);
            if (SelectedListingId != listingId) return;
            if (listing is null) { StatusMessage = "Anúncio não encontrado."; return; }
            try { _listingPrinting = await _catalogClient.GetPrintingAsync(listing.PrintingId); }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* Listing data remains available when catalog lookup fails. */ }
            if (SelectedListingId != listingId) return;
            SelectedListing = listing;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { StatusMessage = ApiErrorMessage(ex); }
        finally { IsBusy = false; OnPropertyChanged(nameof(Screen)); }
    }

    [RelayCommand]
    private async Task BuyListingAsync(Guid listingId, CancellationToken cancellationToken)
    {
        if (IsBusy || SelectedListing is not { Status: "active" } listing || listing.Id != listingId) return;
        if (listing.SellerUserId == _sessionState.User?.Id) { StatusMessage = "Você não pode comprar seu próprio anúncio."; return; }
        if (CurrentOrder?.ListingId != listingId) { CurrentOrder = null; CurrentPayment = null; }
        SelectedListingId = listingId;
        var savedAddress = _currentProfile?.DefaultShippingAddress;
        ShippingStreet = savedAddress?.Street ?? string.Empty;
        ShippingNumber = savedAddress?.Number ?? string.Empty;
        ShippingComplement = savedAddress?.Complement ?? string.Empty;
        ShippingNeighborhood = savedAddress?.Neighborhood ?? string.Empty;
        ShippingCity = savedAddress?.City ?? string.Empty;
        ShippingState = savedAddress?.State ?? string.Empty;
        ShippingZipCode = savedAddress?.ZipCode ?? string.Empty;
        ShippingRecipient = savedAddress?.Recipient ?? string.Empty;
        StatusMessage = null;
        await NavigateCommand.ExecuteAsync("checkout");
    }

    [RelayCommand]
    private async Task SubmitCheckoutAsync(CancellationToken cancellationToken)
    {
        if (SelectedListingId is not Guid listingId)
        {
            StatusMessage = "Listing não disponível para checkout.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ShippingStreet) ||
            string.IsNullOrWhiteSpace(ShippingCity) ||
            string.IsNullOrWhiteSpace(ShippingState) ||
            string.IsNullOrWhiteSpace(ShippingZipCode))
        {
            StatusMessage = "Preencha todos os campos de endereço.";
            return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var buyerId = _sessionState.User?.Id;
            var billing = await _paymentsClient.GetCustomerAsync(cancellationToken);
            if (buyerId != _sessionState.User?.Id) return;
            if (billing?.Status != "READY" || !string.IsNullOrWhiteSpace(BillingDocument))
            {
                billing = await _paymentsClient.RegisterCustomerAsync(new(BillingLegalName, BillingDocument), cancellationToken);
                if (buyerId != _sessionState.User?.Id) return;
            }
            if (billing.Status != "READY") { StatusMessage = "Cadastro de cobrança em conciliação. Aguarde antes de criar o pedido."; return; }
            BillingDocument = string.Empty;
            var order = CurrentOrder is { Status: "pending" } saved && saved.ListingId == listingId && saved.BuyerId == buyerId
                ? saved
                : await _ordersClient.CreateOrderAsync(
                    new CreateOrderRequest(listingId, ShippingStreet.Trim(), ShippingNumber.Trim(), ShippingComplement.Trim(), ShippingNeighborhood.Trim(), ShippingCity.Trim(), ShippingState.Trim(), ShippingZipCode.Trim(), ShippingRecipient.Trim()), cancellationToken);
            if (buyerId != _sessionState.User?.Id) return;
            CurrentOrder = order;

            var payment = await _paymentsClient.InitiatePaymentAsync(
                new CreatePaymentRequest(order.Id, "PIX", null), cancellationToken);
            if (buyerId != _sessionState.User?.Id) return;
            CurrentPayment = payment;

            StatusMessage = !string.IsNullOrWhiteSpace(payment.PixQrCode)
                ? "Copie o código Pix e pague no seu banco. Depois, atualize o pagamento."
                : "Cobrança criada. Abra a cobrança ou tente carregar o código Pix novamente.";

            try
            {
                var tokens = await _tokenStore.GetAsync(CancellationToken.None);
                if (tokens is not null)
                {
                    await _apiClient.UpdateShippingAddressAsync(
                        tokens.AccessToken,
                        ShippingStreet.Trim(),
                        ShippingNumber.Trim(),
                        ShippingComplement.Trim(),
                        ShippingNeighborhood.Trim(),
                        ShippingCity.Trim(),
                        ShippingState.Trim(),
                        ShippingZipCode.Trim(),
                        ShippingRecipient.Trim(),
                        cancellationToken);
                }
            }
            catch
            {
                // Address save is best-effort; order was already created successfully
            }
        }
        catch (OperationCanceledException)
        {
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
            UpdateFilterChartData(query);
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

    private void UpdateFilterChartData(string? filterLabel)
    {
        var history = _priceHistoryProvider.GetFilterPriceHistory(filterLabel ?? "all", days: 7);
        FilterChartData = history.Select(p => new Vaulta.App.Views.Components.ChartPoint(p.Date, p.Value, p.Label)).ToList();
    }

    private async Task LoadCardDetailAsync(Guid printingId)
    {
        if (_loadedPrintingId == printingId && SelectedPrinting is not null) return;

        IsBusy = true;
        try
        {
            var scannerDetails = await _scannerClient.GetCardDetailsAsync(printingId, CancellationToken.None);
            ScannerCardDetails = scannerDetails;
            var details = scannerDetails.Printing;
            SelectedPrinting = details;
            _loadedPrintingId = printingId;
            VariantLabels.Clear();
            _variantIdsByLabel.Clear();
            foreach (var variant in details.Variants)
            {
                VariantLabels.Add(variant.Name);
                _variantIdsByLabel[variant.Name] = variant.Id;
            }
            SelectedVariantLabel = details.Variants.FirstOrDefault(x => x.Code == "normal")?.Name
                ?? details.Variants.FirstOrDefault(x => x.Code == "holo")?.Name
                ?? VariantLabels.FirstOrDefault();
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
        var owner = _sessionState.User?.Id;
        if (owner is null) return;
        var request = RenewRequest(ref _collectionReadCts);
        _collectionValueCts?.Cancel(); CollectionValuation = null; _collectionValueError = null;
        IsBusy = true;
        try
        {
            var page = await _collectionClient.GetCollectionAsync(new CollectionQuery(null, null, null, null, null, 1, 50, "recent"), request.Token);
            if (!OwnsRequest(owner, request)) return;
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
            try
            {
                var summary = await _collectionClient.GetSummaryAsync(request.Token);
                if (!OwnsRequest(owner, request)) return;
                CollectionSummary = summary;
            }
            catch (OperationCanceledException) { throw; }
            catch { CollectionSummary = null; }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (OwnsRequest(owner, request)) StatusMessage = ApiErrorMessage(exception);
        }
        finally
        {
            if (OwnsRequest(owner, request) && ReferenceEquals(request, _collectionReadCts))
            {
                IsBusy = false; _collectionLoadedOnce = true; OnPropertyChanged(nameof(Screen));
            }
        }
        if (OwnsRequest(owner, request)) _ = LoadValuationAsync();
    }

    private async Task LoadCollectionEntryAsync(Guid entryId)
    {
        var owner = _sessionState.User?.Id;
        if (owner is null) return;
        var request = RenewRequest(ref _entryReadCts);
        _entryValueCts?.Cancel(); EntryValuation = null; _entryValueError = null;
        IsBusy = true;
        try
        {
            var entry = await _collectionClient.GetEntryAsync(entryId, 1, 20, request.Token);
            if (!OwnsRequest(owner, request) || SelectedEntryId != entryId) return;
            SelectedEntry = entry;
            var firstItem = entry.Items.FirstOrDefault();
            SelectedItem = firstItem;
            SelectedCondition = firstItem is not null ? ConditionMapper.ToUiLabel(firstItem.Condition) : SelectedCondition;
            ItemNotes = firstItem?.Notes ?? string.Empty;
            OnPropertyChanged(nameof(Screen));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (OwnsRequest(owner, request) && SelectedEntryId == entryId) StatusMessage = ApiErrorMessage(exception);
        }
        finally
        {
            if (OwnsRequest(owner, request) && ReferenceEquals(request, _entryReadCts)) IsBusy = false;
        }
        if (OwnsRequest(owner, request) && SelectedEntryId == entryId) _ = LoadValuationAsync(entryId);
    }

    private async Task LoadHomeSummaryAsync()
    {
        var owner = _sessionState.User?.Id;
        if (owner is null) return;
        var request = RenewRequest(ref _collectionReadCts);
        try
        {
            var summary = await _collectionClient.GetSummaryAsync(request.Token);
            if (!OwnsRequest(owner, request)) return;
            CollectionSummary = summary;
            OnPropertyChanged(nameof(Screen));
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (OwnsRequest(owner, request)) { CollectionSummary = null; OnPropertyChanged(nameof(Screen)); }
        }
        if (OwnsRequest(owner, request)) _ = LoadValuationAsync();
    }

    private static CancellationTokenSource RenewRequest(ref CancellationTokenSource? previous)
    {
        previous?.Cancel(); previous?.Dispose(); return previous = new CancellationTokenSource();
    }

    private bool OwnsRequest(Guid? owner, CancellationTokenSource request) => !request.IsCancellationRequested && owner == _sessionState.User?.Id;

    private async Task LoadValuationAsync(Guid? entryId = null)
    {
        var owner = _sessionState.User?.Id;
        if (owner is null) return;
        var request = entryId.HasValue ? RenewRequest(ref _entryValueCts) : RenewRequest(ref _collectionValueCts);
        if (entryId.HasValue) { EntryValuation = null; _entryValueError = null; }
        else { CollectionValuation = null; _collectionValueError = null; }
        OnPropertyChanged(nameof(Screen));
        try
        {
            var valuation = await _collectionClient.GetValuationAsync(entryId, request.Token);
            if (!OwnsRequest(owner, request) || entryId.HasValue && SelectedEntryId != entryId) return;
            if (valuation.Currency != "BRL") throw new InvalidDataException("Collection valuation must be in BRL.");
            if (entryId.HasValue) EntryValuation = valuation; else CollectionValuation = valuation;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!OwnsRequest(owner, request)) return;
            if (entryId.HasValue) _entryValueError = ApiErrorMessage(ex); else _collectionValueError = ApiErrorMessage(ex);
        }
        finally { if (OwnsRequest(owner, request)) OnPropertyChanged(nameof(Screen)); }
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
        if (SelectedPrinting is null || SelectedPrinting.PrintingId != printingId)
        {
            StatusMessage = "Aguarde o carregamento da carta antes de adicionar.";
            return;
        }
        if (SelectedPrinting.Variants.Count > 0 && SelectedVariantId is null)
        {
            StatusMessage = "Selecione a variante da carta.";
            return;
        }
        if (!int.TryParse(Quantity, out var quantity) || quantity < 1)
        {
            StatusMessage = "Informe uma quantidade válida.";
            return;
        }

        AcquisitionPrice? acquisitionPrice = null;
        if (!string.IsNullOrWhiteSpace(AcquisitionCost))
        {
            if (!decimal.TryParse(AcquisitionCost, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), out var cost) || cost < 0)
            {
                StatusMessage = "Informe um custo de aquisição válido em reais.";
                return;
            }
            acquisitionPrice = new AcquisitionPrice(cost, "BRL");
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
                AcquisitionPrice: acquisitionPrice,
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
        if (SelectedItem.ListedById.HasValue) { StatusMessage = "Cancele o anúncio antes de alterar esta carta."; return; }

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
        if (SelectedItem.ListedById.HasValue) { StatusMessage = "Cancele o anúncio antes de remover esta carta."; return; }

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
        "card-detail" or "scanner-card-detail" or "add-to-collection" => MergeCardDetail(baseScreen),
        "collection" => MergeCollection(baseScreen),
        "collection-item" => MergeCollectionItem(baseScreen),
        "home" => MergeHome(baseScreen),
        "portfolio" => MergePortfolio(baseScreen),
        "profile" => MergeProfile(baseScreen),
        "orders" => MergeOrders(baseScreen),
        "listing-detail" => MergeListingDetail(baseScreen),
        _ => baseScreen
    };

    private ScreenDefinition MergeListingDetail(ScreenDefinition baseScreen)
    {
        if (SelectedListing is not { } listing) return baseScreen;
        var culture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var rating = listing.SellerTotalReviews == 0 ? "Sem avaliações como vendedor"
            : $"{listing.SellerAverageRating.ToString("N2", culture)} / 5 · {listing.SellerTotalReviews} avaliações";
        return baseScreen with
        {
            Title = _listingPrinting?.CardName ?? "Detalhes do anúncio",
            Subtitle = _listingPrinting is { } printing ? $"{printing.SetName} · {printing.CollectorNumber}" : "Confira as fotos e a descrição do vendedor.",
            Notice = listing.Description,
            Metrics = [new("PREÇO", listing.PriceBrl.ToString("C2", culture)), new("CONDIÇÃO", ConditionMapper.ToUiLabel(listing.Condition)), new("VENDEDOR", rating)],
            Actions = listing.Status == "active" && listing.SellerUserId != _sessionState.User?.Id
                ? [new("Comprar agora", null, true)] : null
        };
    }

    private async Task LoadProfileAsync()
    {
        var userId = _sessionState.User?.Id;
        if (_currentProfile is not null || !_sessionState.IsAuthenticated) return;
        IsBusy = true;
        try
        {
            var tokens = await _tokenStore.GetAsync(CancellationToken.None);
            if (tokens is null) return;
            var profile = await _apiClient.GetCurrentUserAsync(tokens.AccessToken, CancellationToken.None);
            if (userId != _sessionState.User?.Id) return;
            _currentProfile = profile;
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

    private async Task LoadOrdersAsync()
    {
        var userId = _sessionState.User?.Id;
        if (_userOrders is not null || !_sessionState.IsAuthenticated) return;
        IsBusy = true;
        try
        {
            var orders = await _ordersClient.GetUserOrdersAsync(page: _orderPage, cancellationToken: CancellationToken.None);
            if (userId != _sessionState.User?.Id) return;
            _userOrders = orders;
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

    private ScreenDefinition MergeProfile(ScreenDefinition baseScreen)
    {
        if (_currentProfile is null) return baseScreen;
        var profile = _currentProfile;
        var interests = profile.TcgInterests.Length > 0 ? string.Join(", ", profile.TcgInterests) : "Nenhum TCG selecionado";
        var location = !string.IsNullOrWhiteSpace(profile.Location?.City) && !string.IsNullOrWhiteSpace(profile.Location?.State)
            ? $"{profile.Location.City}, {profile.Location.State}"
            : "Localização não informada";
        return baseScreen with
        {
            Title = profile.DisplayName,
            Subtitle = $"@{profile.Username} · {location}",
            Metrics = [new("INTERESSES", interests), new("MOEDA", profile.Preferences.Currency), new("IDIOMA", profile.Preferences.Language)],
            Actions = [new("Editar perfil", "settings", true), new("Configurações", "settings"), new("Minha coleção", "collection"), new("Meus pedidos", "orders"), new("Meus repasses", "payouts")]
        };
    }

    private ScreenDefinition MergeOrders(ScreenDefinition baseScreen)
    {
        if (_userOrders is null) return baseScreen;
        if (_userOrders.Items.Count == 0)
            return baseScreen with { Title = "Meus Pedidos", Subtitle = "Você ainda não realizou nenhum pedido.", Cards = null, Metrics = null };

        var cards = _userOrders.Items.Select(order => new ScreenCard(
            order.Status.ToUpperInvariant(),
            $"R$ {order.Snapshot.TotalAmountBrl:N2}",
            $"{order.CreatedAt:dd/MM/yyyy HH:mm} · {order.Snapshot.Currency}",
            order.TrackingCode ?? "Sem rastreio")).ToArray();
        return baseScreen with
        {
            Title = "Meus Pedidos",
            Subtitle = $"{_userOrders.TotalCount} pedido(s) encontrado(s)",
            Cards = cards,
            Metrics = [new("TOTAL", _userOrders.TotalCount.ToString()), new("PÁGINA", $"{_userOrders.Page}/{Math.Max(1, (_userOrders.TotalCount + _userOrders.PageSize - 1) / _userOrders.PageSize)}")]
        };
    }

    private ScreenDefinition MergeCardDetail(ScreenDefinition baseScreen)
    {
        if (SelectedPrinting is null) return baseScreen;
        var printing = SelectedPrinting;
        return baseScreen with
        {
            Title = printing.CardName,
            Subtitle = $"{printing.SetName} · {printing.CollectorNumber} · {FormatGame(printing.GameCode)}",
            Options = baseScreen.Id == "add-to-collection" ? baseScreen.Options : (VariantLabels.Count > 0 ? VariantLabels.ToArray() : null)
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

        var metrics = new ScreenMetric[] { new("CARTAS", (CollectionValuation?.TotalItems ?? CollectionSummary?.TotalItems)?.ToString() ?? "—"),
            ValuationMetric("VALOR ESTIMADO", CollectionValuation, _collectionValueError) };
        return baseScreen with { Cards = CollectionEntries.ToArray(), Metrics = metrics, Notice = ValuationNotice(CollectionValuation, _collectionValueError) };
    }

    private ScreenDefinition MergeCollectionItem(ScreenDefinition baseScreen)
    {
        if (SelectedEntry is null) return baseScreen;
        var entry = SelectedEntry;
        var conditionLabel = SelectedItem is not null ? ConditionMapper.ToUiLabel(SelectedItem.Condition) : "—";
        var listed = SelectedItem?.ListedById.HasValue == true;
        return baseScreen with
        {
            Title = entry.CardName,
            Subtitle = $"{entry.SetName} · {entry.CollectorNumber} · {conditionLabel}",
            Metrics = [new ScreenMetric("QUANTIDADE", entry.Quantity.ToString()), ValuationMetric("VALOR ESTIMADO DAS UNIDADES", EntryValuation, _entryValueError)],
            Notice = (listed ? "Esta carta está anunciada. Cancele o anúncio para alterar ou remover o item. " : "") + ValuationNotice(EntryValuation, _entryValueError),
            Options = listed ? null : ConditionMapper.UiLabels.ToArray(),
            Actions = listed ? null : [new ScreenAction("Salvar alterações", "collection-item-update", true), new ScreenAction("Remover da coleção", "collection-item-remove")]
        };
    }

    private ScreenDefinition MergeHome(ScreenDefinition baseScreen)
    {
        var metrics = new ScreenMetric[] { ValuationMetric("ESTIMATIVA TOTAL", CollectionValuation, _collectionValueError),
            new("CARTAS", (CollectionValuation?.TotalItems ?? CollectionSummary?.TotalItems)?.ToString() ?? "—", "na coleção") };
        return baseScreen with { Metrics = metrics, Notice = ValuationNotice(CollectionValuation, _collectionValueError) };
    }

    private ScreenDefinition MergePortfolio(ScreenDefinition baseScreen) => baseScreen with
    {
        Metrics = [ValuationMetric("TOTAL ESTIMADO", CollectionValuation, _collectionValueError),
            new("CARTAS AVALIADAS", CollectionValuation is { } value ? $"{value.PricedItems} / {value.TotalItems}" : "—",
                CollectionValuation?.UnpricedItems > 0 ? $"{CollectionValuation.UnpricedItems} sem cotação ou variante confirmada" : "Unidades ativas no estoque")],
        Cards = null, Notice = ValuationNotice(CollectionValuation, _collectionValueError)
    };

    private static ScreenMetric ValuationMetric(string label, CollectionValuationDto? value, string? error)
    {
        if (value is null) return new(label, error is null ? "Calculando…" : "Indisponível", error);
        var money = value.PricedItems == 0 && value.TotalItems > 0 ? "Indisponível" : FormatBrl(value.EstimatedValueBrl);
        var coverage = value.IsPartial ? $"Parcial · {value.PricedItems} de {value.TotalItems} cartas avaliadas" : $"{value.PricedItems} cartas avaliadas";
        return new(label, money, coverage + (value.OldestQuoteAt is { } quote ? $" · cotação mais antiga {quote.ToLocalTime():dd/MM HH:mm}" : ""));
    }

    private static string ValuationNotice(CollectionValuationDto? value, string? error) => error ?? value?.Notice ?? "Consultando referências de mercado em reais…";
    public static string FormatBrl(decimal value) => value.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

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
        if (route == "collection-valuation-refresh")
        {
            await LoadValuationAsync(); return;
        }
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

        if (route == "scanner")
        {
            await Shell.Current.GoToAsync("scanner-session");
            return;
        }

        if (route is "welcome" or "login")
        {
            await Shell.Current.GoToAsync("experience?screen=login");
            return;
        }

        var printingQuery = route is "card-detail" or "scanner-card-detail" or "add-to-collection" && SelectedPrintingId is { } selectedId
            ? $"&printingId={selectedId}"
            : string.Empty;
        await Shell.Current.GoToAsync($"experience?screen={Uri.EscapeDataString(route)}{printingQuery}");
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
            ClearPrivateCommerceData();
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
            ClearPrivateCommerceData();
            await Shell.Current.GoToAsync("//main/home/home-page");
        });
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (IsBusy) return;
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
            ClearPrivateCommerceData();
            IsBusy = false;
            await ((AppShell)Shell.Current).ShowLoginAsync();
        }
    }

    private void ClearPrivateCommerceData()
    {
        _collectionReadCts?.Cancel(); _collectionValueCts?.Cancel(); _entryReadCts?.Cancel(); _entryValueCts?.Cancel();
        CollectionEntries.Clear(); CollectionSummary = null; CollectionValuation = null; EntryValuation = null;
        SelectedEntry = null; SelectedItem = null; SelectedEntryId = null; ItemNotes = string.Empty;
        _collectionValueError = null; _entryValueError = null; _collectionLoadedOnce = false;
        _currentProfile = null;
        _userOrders = null;
        PixDestination = null;
        SellerPayouts = null;
        PixKey = string.Empty;
        _payoutPage = 1;
        _orderPage = 1;
        CurrentOrder = null;
        CurrentPayment = null;
        BillingLegalName = string.Empty;
        BillingDocument = string.Empty;
    }

    private bool EnsurePrivateDataOwner()
    {
        var userId = _sessionState.User?.Id;
        if (_privateDataOwner == userId) return false;
        _privateDataOwner = userId;
        ClearPrivateCommerceData();
        return true;
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

    [RelayCommand]
    private async Task PickSellPhotoAsync()
    {
        try
        {
            var photo = await MediaPicker.CapturePhotoAsync(new MediaPickerOptions { Title = "Foto do card" });
            if (photo is null) return;

            using var stream = await photo.OpenReadAsync();
            var length = stream.Length;
            var contentType = photo.ContentType ?? "image/jpeg";

            var upload = await _assetClient.CreateUploadAsync(
                new CreateAssetUploadRequest("collection-item", contentType, length, null));

            stream.Position = 0;
            await _assetClient.UploadToPresignedUrlAsync(upload.UploadUrl, stream, contentType);
            await _assetClient.ConfirmUploadAsync(upload.AssetId);

            _sellPhotoAssetIds.Add(upload.AssetId);
            StatusMessage = $"Foto adicionada ({_sellPhotoAssetIds.Count}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao adicionar foto: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SubmitListingAsync()
    {
        if (IsBusy) return;
        if (_sellPhotoAssetIds.Count == 0)
        {
            StatusMessage = "Adicione pelo menos uma foto antes de publicar.";
            return;
        }
        if (SelectedPrintingId is null)
        {
            StatusMessage = "Selecione um card para vender.";
            return;
        }
        if (!decimal.TryParse(AcquisitionCost, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), out var priceBrl) || priceBrl <= 0)
        {
            StatusMessage = "Informe um preço válido.";
            return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var collectibleItemId = SelectedEntry?.Items.FirstOrDefault()?.Id ?? Guid.Empty;
            var variantId = (!string.IsNullOrWhiteSpace(SelectedVariantLabel) &&
                            _variantIdsByLabel.TryGetValue(SelectedVariantLabel, out var vid)) ? (Guid?)vid : null;

            var listing = await _marketplaceClient.CreateListingAsync(new CreateListingRequest(
                collectibleItemId,
                SelectedPrintingId.Value,
                variantId,
                SelectedCondition.Trim(),
                priceBrl,
                string.IsNullOrWhiteSpace(ItemNotes) ? null : ItemNotes.Trim()));

            for (var i = 0; i < _sellPhotoAssetIds.Count; i++)
            {
                await _marketplaceClient.AddListingPhotoAsync(listing.Id, new ListingPhotoRequest(
                    _sellPhotoAssetIds[i],
                    i == 0 ? "FRONT" : "OTHER",
                    i));
            }

            _sellPhotoAssetIds.Clear();
            ItemNotes = string.Empty;
            SelectedCondition = "Near Mint";
            AcquisitionCost = string.Empty;
            StatusMessage = "Anúncio publicado com sucesso!";
            await Shell.Current.GoToAsync("//main/market/market-page");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao publicar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

