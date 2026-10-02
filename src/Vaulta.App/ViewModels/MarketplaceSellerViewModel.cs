using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vaulta.App.Core.Marketplace;

namespace Vaulta.App.ViewModels;

/// <summary>Only public listing data is shown; private /me/seller is never used for another person.</summary>
public partial class MarketplaceSellerViewModel(MarketplaceHomeController controller) : ObservableObject
{
    private bool _active;
    private Guid? _sellerId;
    public ObservableCollection<MarketplaceListingPresentation> Listings { get; } = [];
    [ObservableProperty] private string seller = "Vendedor";
    [ObservableProperty] private string rating = "Avaliações indisponíveis";
    [ObservableProperty] private string count = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private bool hasProblem;
    [ObservableProperty] private bool showInitialProblem;
    [ObservableProperty] private bool showFooterProblem;
    [ObservableProperty] private string? statusMessage;

    public void Configure(Guid sellerId) { _sellerId = sellerId; Seller = $"Vendedor {sellerId.ToString("N")[..8]}"; }
    public async Task ActivateAsync()
    {
        if (!_active) { _active = true; controller.StateChanged += Changed; }
        await RefreshAsync();
    }
    public void Deactivate() { _active = false; controller.StateChanged -= Changed; controller.CancelPending(); }
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RefreshAsync()
    {
        if (_sellerId is { } sellerId) return controller.RefreshAsync(new(null, null, "newest", sellerId));
        HasProblem = true; ShowInitialProblem = true; StatusMessage = "Vendedor não encontrado.";
        return Task.CompletedTask;
    }
    [RelayCommand] private Task RetryAsync() => controller.RetryAsync();
    [RelayCommand] private Task LoadMoreAsync() => controller.LoadMoreAsync();
    private void Changed(object? sender, EventArgs args) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!_active) return;
        var state = controller.State;
        if (state.Filters.SellerUserId != _sellerId) return;
        IsLoading = state.IsLoading; IsLoadingMore = state.IsLoadingMore; IsEmpty = state.IsEmpty;
        HasProblem = state.Problem != MarketplaceHomeProblem.None; StatusMessage = state.Message;
        ShowInitialProblem = HasProblem && state.Items.Count == 0;
        ShowFooterProblem = HasProblem && state.Items.Count > 0;
        HasMore = state.HasMore && !state.IsBusy && !HasProblem;
        Count = state.HasLoaded ? $"{state.TotalCount} anúncio{(state.TotalCount == 1 ? " ativo" : "s ativos")}" : "";
        var listings = state.Items.Where(item => item.SellerUserId == _sellerId).ToArray();
        var reputation = listings.FirstOrDefault();
        Rating = reputation is null ? "Avaliações indisponíveis" : reputation.SellerTotalReviews == 0 ? "Sem avaliações como vendedor"
            : $"{reputation.SellerAverageRating.ToString("N1", CultureInfo.GetCultureInfo("pt-BR"))} / 5 · {reputation.SellerTotalReviews} avaliações de vendedor";
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < listings.Length; index++)
        {
            var mapped = MarketplaceListingPresentation.From(listings[index], now);
            if (index >= Listings.Count) Listings.Add(mapped); else if (Listings[index] != mapped) Listings[index] = mapped;
        }
        while (Listings.Count > listings.Length) Listings.RemoveAt(Listings.Count - 1);
    });
}
