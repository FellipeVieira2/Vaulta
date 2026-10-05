using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vaulta.App.Core.Marketplace;

namespace Vaulta.App.ViewModels;

public partial class MarketplaceHomeViewModel(MarketplaceProductBrowseController controller) : ObservableObject
{
    private CancellationTokenSource? _debounce;
    private bool _active;
    public ObservableCollection<MarketplaceProductPresentation> Products { get; } = [];
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string? selectedGameCode = "pokemon";
    [ObservableProperty] private string selectedSort = "newest";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private bool hasProblem;
    [ObservableProperty] private bool showInitialProblem;
    [ObservableProperty] private bool showFooterProblem;
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private string statusTitle = "";
    [ObservableProperty] private string sectionTitle = "Recém anunciadas";
    [ObservableProperty] private string resultSummary = "";

    public async Task ActivateAsync()
    {
        if (!_active) { _active = true; controller.StateChanged += ControllerChanged; }
        await RefreshAsync();
    }

    public void Deactivate()
    {
        _active = false;
        CancelDebounce();
        controller.StateChanged -= ControllerChanged;
        controller.CancelPending();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_active) return;
        CancelDebounce();
        controller.CancelPending();
        var cancellation = new CancellationTokenSource();
        _debounce = cancellation;
        _ = DebouncedSearchAsync(cancellation);
    }

    partial void OnSelectedGameCodeChanged(string? value) { if (_active) _ = RefreshAsync(); }
    partial void OnSelectedSortChanged(string value) { if (_active) _ = RefreshAsync(); }

    private async Task DebouncedSearchAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(300, cancellation.Token);
            if (_active) await controller.RefreshAsync(Filters(), cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_debounce, cancellation)) _debounce = null;
            cancellation.Dispose();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RefreshAsync()
    {
        CancelDebounce();
        return controller.RefreshAsync(Filters());
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync() => RefreshAsync();
    [RelayCommand] private Task LoadMoreAsync() => controller.LoadMoreAsync();
    [RelayCommand] private Task RetryAsync() => controller.RetryAsync();

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        // One refresh after updating all controls; intermediate combinations must not issue calls.
        var active = _active;
        _active = false;
        _advanced = new(); SearchText = ""; SelectedGameCode = "pokemon"; SelectedSort = "newest";
        _active = active;
        await RefreshAsync();
    }

    private MarketplaceProductFilters _advanced = new();
    public MarketplaceProductFilters CurrentFilters => _advanced with { Query = SearchText, GameCode = SelectedGameCode, Sort = SelectedSort };
    public Task ApplyFiltersAsync(MarketplaceProductFilters filters) { _advanced = filters; return _active ? RefreshAsync() : Task.CompletedTask; }
    private MarketplaceProductFilters Filters() => CurrentFilters;
    private void CancelDebounce()
    {
        var cancellation = _debounce;
        _debounce = null;
        cancellation?.Cancel();
    }

    private void ControllerChanged(object? sender, EventArgs args) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!_active) return;
        var state = controller.State;
        IsLoading = state.IsLoading; IsLoadingMore = state.IsLoadingMore; IsEmpty = state.IsEmpty;
        HasProblem = state.Problem != MarketplaceHomeProblem.None;
        ShowInitialProblem = HasProblem && state.Items.Count == 0;
        ShowFooterProblem = HasProblem && state.Items.Count > 0;
        StatusTitle = state.Problem == MarketplaceHomeProblem.Offline ? "Você está offline" : "Não foi possível carregar";
        StatusMessage = ShowFooterProblem ? $"{state.Message} As cartas já carregadas continuam disponíveis." : state.Message;
        HasMore = state.HasMore && !state.IsBusy && !HasProblem;
        SectionTitle = string.IsNullOrWhiteSpace(state.Filters.Query) && state.Filters.Sort == "newest" ? "Cartas à venda" : "Cartas encontradas";
        ResultSummary = state.HasLoaded ? $"{state.TotalCount} carta{(state.TotalCount == 1 ? "" : "s")}" : "";

        for (var index = 0; index < state.Items.Count; index++)
        {
            var item = MarketplaceProductPresentation.From(state.Items[index]);
            if (index >= Products.Count) Products.Add(item);
            else if (Products[index] != item) Products[index] = item;
        }
        while (Products.Count > state.Items.Count) Products.RemoveAt(Products.Count - 1);
    });
}
