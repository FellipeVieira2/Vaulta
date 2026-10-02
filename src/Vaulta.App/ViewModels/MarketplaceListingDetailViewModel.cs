using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vaulta.App.Core.Marketplace;
using Vaulta.App.State;
using Vaulta.Marketplace.Contracts;

namespace Vaulta.App.ViewModels;

public sealed record MarketplaceListingPhoto(string Url, string Description, bool IsCatalogReference = false);

public partial class MarketplaceListingDetailViewModel(MarketplaceListingDetailController controller, SessionState session) : ObservableObject
{
    private bool _active;
    private Guid? _listingId;
    public ObservableCollection<MarketplaceListingPresentation> Comparisons { get; } = [];
    public ObservableCollection<MarketplaceListingPhoto> Photos { get; } = [];
    [ObservableProperty] private ListingDto? listing;
    [ObservableProperty] private MarketplaceListingPresentation? card;
    [ObservableProperty] private MarketplaceListingPhoto? selectedPhoto;
    [ObservableProperty] private string? imageUrl;
    [ObservableProperty] private string imageDescription = "Sem imagem disponível";
    [ObservableProperty] private bool showImagePlaceholder = true;
    [ObservableProperty] private bool hasImage;
    [ObservableProperty] private string photoNotice = "";
    [ObservableProperty] private string printingDetail = "";
    [ObservableProperty] private string seller = "";
    [ObservableProperty] private string rating = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool hasListing;
    [ObservableProperty] private bool hasProblem;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool isComparing;
    [ObservableProperty] private bool hasComparisonProblem;
    [ObservableProperty] private string? comparisonMessage;
    [ObservableProperty] private bool comparisonEmpty;
    [ObservableProperty] private bool hasMoreComparisons;
    [ObservableProperty] private bool canBuy;
    [ObservableProperty] private string buyLabel = "Comprar agora";
    [ObservableProperty] private string? metadataMessage;
    [ObservableProperty] private bool hasMetadataMessage;
    [ObservableProperty] private string description = "O vendedor não adicionou uma descrição.";

    public void Configure(Guid listingId) => _listingId = listingId;
    public async Task ActivateAsync()
    {
        if (!_active)
        {
            _active = true; controller.StateChanged += Changed; session.PropertyChanged += SessionChanged;
        }
        await RefreshAsync();
    }
    public void Deactivate()
    {
        _active = false; controller.StateChanged -= Changed; session.PropertyChanged -= SessionChanged; controller.CancelPending();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RefreshAsync()
    {
        if (_listingId is { } id) return controller.LoadAsync(id);
        HasProblem = true; StatusMessage = "Anúncio não encontrado.";
        return Task.CompletedTask;
    }
    [RelayCommand] private Task RetryComparisonsAsync() => controller.RetryComparisonsAsync();
    [RelayCommand] private Task LoadMoreComparisonsAsync() => controller.LoadMoreComparisonsAsync();
    [RelayCommand] private void SelectPhoto(MarketplaceListingPhoto? photo) => SelectedPhoto = photo;
    partial void OnSelectedPhotoChanged(MarketplaceListingPhoto? value)
    {
        ImageUrl = value?.Url; ImageDescription = value?.Description ?? "Sem imagem disponível";
        ShowImagePlaceholder = value is null;
        HasImage = value is not null;
    }
    private void SessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SessionState.User)) MainThread.BeginInvokeOnMainThread(UpdatePurchase);
    }
    private void UpdatePurchase()
    {
        CanBuy = Listing is { Status: "active" } && Listing.SellerUserId != session.User?.Id && !IsLoading;
        BuyLabel = Listing?.SellerUserId == session.User?.Id ? "Seu anúncio"
            : Listing?.Status != "active" ? "Anúncio indisponível" : session.IsAuthenticated ? "Comprar agora" : "Entrar para comprar";
    }
    private void Changed(object? sender, EventArgs args) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!_active) return;
        var state = controller.State;
        IsLoading = state.IsLoading; Listing = state.Listing; HasListing = state.Listing is not null && !IsLoading;
        HasProblem = state.Message is not null; StatusMessage = state.Message;
        MetadataMessage = state.MetadataMessage; HasMetadataMessage = state.MetadataMessage is not null;
        IsComparing = state.IsComparing; ComparisonMessage = state.ComparisonMessage;
        HasComparisonProblem = ComparisonMessage is not null;
        HasMoreComparisons = state.HasMoreComparisons && !IsComparing && !HasComparisonProblem;
        ComparisonEmpty = HasListing && !IsComparing && !HasComparisonProblem && !state.HasMoreComparisons && state.Comparisons.Count == 0;
        var now = DateTimeOffset.UtcNow;
        var mapped = state.Comparisons.Select(item => MarketplaceListingPresentation.From(item, now)).ToArray();
        for (var index = 0; index < mapped.Length; index++)
        {
            if (index >= Comparisons.Count) Comparisons.Add(mapped[index]);
            else if (Comparisons[index] != mapped[index]) Comparisons[index] = mapped[index];
        }
        while (Comparisons.Count > mapped.Length) Comparisons.RemoveAt(Comparisons.Count - 1);
        if (Listing is { } listing)
        {
            Card = MarketplaceListingPresentation.From(listing, now);
            PrintingDetail = listing.Printing is { } printing
                ? $"{printing.CollectorNumber} · {printing.SetName}{(string.IsNullOrWhiteSpace(printing.VariantCode) ? "" : " · " + printing.VariantCode)}" : "Dados da carta indisponíveis";
            Seller = $"Vendedor {listing.SellerUserId.ToString("N")[..8]}";
            Rating = listing.SellerTotalReviews == 0 ? "Sem avaliações como vendedor"
                : $"{listing.SellerAverageRating.ToString("N1", CultureInfo.GetCultureInfo("pt-BR"))} / 5 · {listing.SellerTotalReviews} avaliações de vendedor";
            Description = string.IsNullOrWhiteSpace(listing.Description) ? "O vendedor não adicionou uma descrição." : listing.Description;
            var photos = listing.Photos.Where(photo => photo.UrlExpiresAt > now && WebUrl(photo.Url))
                .OrderByDescending(photo => photo.IsPrimary).ThenBy(photo => photo.SortOrder)
                .Select(photo => new MarketplaceListingPhoto(photo.Url, photo.Type.ToLowerInvariant() switch
                { "front" => "Frente · foto do vendedor", "back" => "Verso · foto do vendedor", _ => "Detalhe · foto do vendedor" })).ToList();
            if (photos.Count == 0 && Card.ImageUrl is { } reference) photos.Add(new(reference, "Imagem de referência do catálogo", true));
            var selectedUrl = SelectedPhoto?.Url;
            Photos.Clear(); foreach (var photo in photos) Photos.Add(photo);
            SelectedPhoto = Photos.FirstOrDefault(photo => photo.Url == selectedUrl) ?? Photos.FirstOrDefault();
            PhotoNotice = photos.Count == 0 ? "Fotos da unidade indisponíveis."
                : photos[0].IsCatalogReference ? "Imagem de referência. Fotos da unidade indisponíveis."
                : listing.Photos.Any(photo => photo.Type.Equals("front", StringComparison.OrdinalIgnoreCase) && photo.UrlExpiresAt > now && WebUrl(photo.Url))
                    && listing.Photos.Any(photo => photo.Type.Equals("back", StringComparison.OrdinalIgnoreCase) && photo.UrlExpiresAt > now && WebUrl(photo.Url))
                    ? "Confira frente, verso e condição nas fotos do vendedor." : "Fotos de frente/verso incompletas neste anúncio.";
        }
        else { Card = null; Photos.Clear(); SelectedPhoto = null; }
        UpdatePurchase();
    });

    private static bool WebUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
