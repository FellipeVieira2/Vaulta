using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vaulta.App.Core.Assets;
using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Http;
using Vaulta.App.Core.Marketplace;
using Vaulta.App.Services.Camera;
using Vaulta.App.State;
using Vaulta.Marketplace.Contracts;

namespace Vaulta.App.ViewModels;

public partial class ScannerSaleViewModel(ScannerSaleFlow flow, IListingDraftClient drafts, IAssetClient assets,
    IScannerSessionStore sessions, SessionState account, ICameraService camera, IMarketplaceClient marketplace) : ObservableObject
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private CancellationTokenSource _lifetime = new();
    private ScannerSession? _session;
    private Guid _scanId;
    private bool _subscribed;
    private SellerProfileDto? _seller;
    private ListingDraftDto? _draft;
    public static IReadOnlyList<string> Conditions { get; } = ["Mint", "Near Mint", "Pouco jogada", "Jogada", "Muito jogada", "Danificada"];
    private static readonly string[] ConditionCodes = ["MINT", "NEAR_MINT", "LIGHTLY_PLAYED", "MODERATELY_PLAYED", "HEAVILY_PLAYED", "DAMAGED"];
    [ObservableProperty] private ScannerSessionCard? card;
    [ObservableProperty] private int conditionIndex = -1;
    [ObservableProperty] private string priceText = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isReview;
    [ObservableProperty] private bool activateSeller;
    [ObservableProperty] private string? statusMessage;
    public bool IsPublished => _draft?.Status is "active" or "sold";
    public bool IsSold => _draft?.Status == "sold";
    public string PublishedActionLabel => IsSold ? "Ver minhas vendas" : "Abrir anúncio";
    public bool CanEdit => _draft?.Status == "draft";
    public bool NeedsSeller => _seller is null;
    public bool HasFront => _draft?.Photos.Any(x => x.Type.Equals("FRONT", StringComparison.OrdinalIgnoreCase)) == true;
    public bool HasBack => _draft?.Photos.Any(x => x.Type.Equals("BACK", StringComparison.OrdinalIgnoreCase)) == true;
    public string? FrontUrl => _draft?.Photos.FirstOrDefault(x => x.Type.Equals("FRONT", StringComparison.OrdinalIgnoreCase))?.Url;
    public string? BackUrl => _draft?.Photos.FirstOrDefault(x => x.Type.Equals("BACK", StringComparison.OrdinalIgnoreCase))?.Url;
    public string MarketReference => Card?.MarketValue is { } q ? $"{q.AmountBrl.ToString("C2", Br)} · {q.Source}\n{q.QuotedAt.ToLocalTime():dd/MM HH:mm} · referência de mercado" : "Sem cotação · informe seu preço";
    public string CardDetails => Card is { } c ? $"{c.SetName} · {c.CollectorNumber} · {c.VariantName}" : "";
    public string ReviewSummary => _draft is { } d ? $"Preço pedido: {d.PriceBrl?.ToString("C2", Br)}\nCondição: {(ConditionIndex >= 0 ? Conditions[ConditionIndex] : "Não avaliada")}\n{d.Description}" : "";
    public Guid? PublishedId => IsPublished ? _draft?.Id : null;

    public Task ActivateAsync(Guid sessionId, Guid scanId) => Run(async () =>
    {
        if (_lifetime.IsCancellationRequested) { _lifetime.Dispose(); _lifetime = new(); }
        if (!_subscribed) { account.PropertyChanged += AccountChanged; _subscribed = true; }
        if (_session is { } previous && previous.OwnerId != account.User?.Id) ClearPrivateState();
        var owner = account.User?.Id ?? throw new InvalidOperationException("Entre na sua conta para vender.");
        var session = await sessions.Latest(owner, _lifetime.Token);
        if (session?.Id != sessionId) throw new InvalidOperationException("Reabra a sessão em que esta carta foi identificada.");
        _session = session; _scanId = scanId; RequireOwner();
        Card = session.Cards.Single(x => x.ScanId == scanId);
        SetDraft(await flow.Prepare(session, scanId, _lifetime.Token)); RequireOwner();
        _seller = await marketplace.GetMySellerProfileAsync(_lifetime.Token); RequireOwner(); NotifyDraft();
        StatusMessage = IsPublished ? "Anúncio publicado. Você pode abrir o anúncio ou continuar a leitura." : "Rascunho privado. Declare a condição e o preço, e fotografe frente e verso.";
    });

    public void Deactivate()
    {
        _lifetime.Cancel();
        if (_subscribed) account.PropertyChanged -= AccountChanged;
        _subscribed = false;
    }

    [RelayCommand] private Task ReloadAsync() => _session is { } s ? ActivateAsync(s.Id, _scanId) : Task.CompletedTask;
    [RelayCommand] private void Edit() { if (CanEdit) IsReview = false; }
    [RelayCommand] private Task SaveReviewAsync() => Run(async () =>
    {
        RequireOwner(); var draft = Editable();
        if (ConditionIndex < 0 || ConditionIndex >= ConditionCodes.Length) throw new ArgumentException("Declare a condição desta unidade.");
        if (!decimal.TryParse(PriceText.Trim(), NumberStyles.AllowDecimalPoint, Br, out var price) || price <= 0 || decimal.Round(price, 2) != price)
            throw new ArgumentException("Informe seu preço em reais, como 25,90, com até duas casas decimais.");
        if (!HasFront || !HasBack) throw new ArgumentException("Adicione fotos reais da frente e do verso antes de revisar.");
        SetDraft(await drafts.UpdateAsync(draft.Id, new(ConditionCodes[ConditionIndex], price,
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(), draft.Version), _lifetime.Token)); RequireOwner();
        IsReview = true; StatusMessage = "Confira a carta, a condição, o preço e as fotos antes de publicar.";
    });

    [RelayCommand] private Task AddPhotoAsync(string type) => Run(async () =>
    {
        RequireOwner(); var draft = Editable();
        if (type is not ("FRONT" or "BACK")) throw new ArgumentException("Escolha frente ou verso.");
        if (draft.Photos.Any(x => x.Type.Equals(type, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Remova a foto atual antes de fotografar novamente.");
        var bytes = await camera.CapturePhotoAsync(_lifetime.Token); RequireOwner();
        if (bytes is null) return;
        if (bytes.Length is < 4 or > 15_000_000) throw new ArgumentException("Use uma foto de até 15 MB.");
        var mime = bytes[0] == 0xff && bytes[1] == 0xd8 ? "image/jpeg"
            : bytes[0] == 0x89 && bytes[1] == 0x50 ? "image/png"
            : bytes.Length > 12 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP" ? "image/webp"
            : throw new ArgumentException("Use uma foto JPEG, PNG ou WebP.");
        var upload = await assets.CreateUploadAsync(new("collection-item", mime, bytes.Length, null), _lifetime.Token); RequireOwner();
        using var stream = new MemoryStream(bytes);
        await assets.UploadToPresignedUrlAsync(upload.UploadUrl, stream, mime, _lifetime.Token); RequireOwner();
        await assets.ConfirmUploadAsync(upload.AssetId, _lifetime.Token); RequireOwner();
        SetDraft(await drafts.AddPhotoAsync(draft.Id, new(upload.AssetId, type, type == "FRONT" ? 0 : 1, draft.Version), _lifetime.Token), loadInputs: false); RequireOwner();
        StatusMessage = type == "FRONT" ? "Foto da frente salva." : "Foto do verso salva.";
    });

    [RelayCommand] private Task RemovePhotoAsync(string type) => Run(async () =>
    {
        RequireOwner(); var draft = Editable(); var photo = draft.Photos.SingleOrDefault(x => x.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        if (photo is null) return;
        SetDraft(await drafts.RemovePhotoAsync(draft.Id, photo.AssetId, draft.Version, _lifetime.Token), loadInputs: false); RequireOwner(); IsReview = false;
    });

    [RelayCommand] private Task PublishAsync() => Run(async () =>
    {
        RequireOwner(); if (_draft is null || _session is null || !IsReview && _draft.Status == "draft") throw new InvalidOperationException("Revise o anúncio antes de publicar.");
        if (_seller is null)
        {
            if (!ActivateSeller) throw new ArgumentException("Ative seu perfil de vendedor para publicar.");
            _seller = await marketplace.EnableSellerAsync(new(null, null, null, null, null), _lifetime.Token); RequireOwner();
        }
        SetDraft(await flow.Publish(_session, _scanId, _draft, _lifetime.Token)); RequireOwner();
        StatusMessage = IsPublished ? "Anúncio publicado!" : "Publicação em processamento. Toque em retomar para consultar o resultado.";
    });

    private ListingDraftDto Editable() => _draft is { Status: "draft" } draft ? draft : throw new InvalidOperationException("Este anúncio não está disponível para edição.");
    private void SetDraft(ListingDraftDto draft, bool loadInputs = true)
    {
        RequireOwner(); _draft = draft;
        if (loadInputs)
        {
            ConditionIndex = Array.IndexOf(ConditionCodes, draft.Condition);
            PriceText = draft.PriceBrl?.ToString("F2", Br) ?? "";
            Description = draft.Description ?? "";
        }
        if (draft.Status is "publishing" or "active" or "sold") IsReview = true;
        NotifyDraft();
    }
    private void NotifyDraft()
    {
        foreach (var name in new[] { nameof(IsPublished), nameof(IsSold), nameof(PublishedActionLabel), nameof(CanEdit), nameof(NeedsSeller), nameof(HasFront), nameof(HasBack), nameof(FrontUrl), nameof(BackUrl), nameof(MarketReference), nameof(CardDetails), nameof(ReviewSummary), nameof(PublishedId) }) OnPropertyChanged(name);
    }
    private void RequireOwner()
    {
        if (_session is null || _session.OwnerId != account.User?.Id) throw new InvalidOperationException("Entre na conta que iniciou esta sessão.");
    }
    private void AccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionState.User) || _session?.OwnerId == account.User?.Id) return;
        _lifetime.Cancel(); ClearPrivateState();
        StatusMessage = "Entre na conta que iniciou esta sessão para retomar a venda.";
    }
    private void ClearPrivateState()
    {
        _session = null; _draft = null; Card = null; PriceText = ""; Description = "";
        _seller = null; ConditionIndex = -1; IsReview = false; ActivateSeller = false; NotifyDraft();
    }
    private async Task Run(Func<Task> action)
    {
        if (IsBusy) return; IsBusy = true; StatusMessage = null;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusMessage = ex is ArgumentException or InvalidOperationException ? ex.Message : ApiErrorTranslator.FromException(ex).Message; }
        finally { IsBusy = false; }
    }
}
