using Vaulta.App.Core.Catalog;

namespace Vaulta.App.Views;

public sealed partial class ScannerSessionPage
{
    private async Task Reveal(ScannerSessionCard card, decimal previousTotal, ScannerOperation operation)
    {
        RequireScannerOperation(operation);
        var panel = _revealPanel;
        if (panel is null) return;
        var presentation = ScannerRevealPresentation.Create(card, previousTotal, _session!.EstimatedValueBrl);
        var generation = ++_revealGeneration;
        _revealInFlight = true;
        bool Current() => generation == _revealGeneration && !operation.Context.Token.IsCancellationRequested
            && _visible && _session?.Id == operation.Session.Id && _account.User?.Id == operation.Session.OwnerId;
        void Check() { RequireScannerOperation(operation); if (!Current()) throw new OperationCanceledException(); }
        _revealTitle!.Text = card.Name;
        _revealPrinting!.Text = string.Join(" · ", new[] { card.SetName, card.CollectorNumber, card.VariantName,
            card.VisualIdentification?.Attributes?.Rarity }.Where(x => !string.IsNullOrWhiteSpace(x)));
        _revealArtwork!.Source = Uri.TryCreate(card.ArtworkUrl,UriKind.Absolute,out var artwork) && artwork.Scheme==Uri.UriSchemeHttps ? ImageSource.FromUri(artwork) : null;
        _revealArtwork.IsVisible = _revealArtwork.Source is not null;
        SemanticProperties.SetDescription(_revealArtwork, $"Imagem de referência de {card.Name}");
        _revealValue.Text = presentation.Kind switch
        {
            ScannerRevealKind.Price => Money(UiMotion.ReducedMotion ? presentation.Value!.Value : 0),
            ScannerRevealKind.VariantPending => "Acabamento pendente",
            ScannerRevealKind.GradedUnavailable => "Cotação da certificação indisponível",
            _ => "Sem cotação disponível"
        };
        _revealValue.FontSize = presentation.Value.HasValue ? 48 : 20;
        _revealValue.SetDynamicResource(Label.TextColorProperty, presentation.Value.HasValue ? "BrandPrimary" : "TextSecondary");
        _revealValue.Opacity = UiMotion.ReducedMotion ? 1 : 0;
        _revealSource!.Text = presentation.Value.HasValue && card.MarketValue is {} quote
            ? $"{quote.Source}{(card.Condition != "UNKNOWN" ? " · " + card.Condition : "")} · atualizado {quote.QuotedAt.ToLocalTime():dd/MM HH:mm}"
            : "Adicionada à sessão · não entra no valor estimado";
        _revealSource.Opacity = _revealValue.Opacity;
        _total.Text = Money(UiMotion.ReducedMotion ? presentation.FinalTotal : previousTotal);
        panel.IsVisible = true; panel.Opacity = 0; panel.Scale = UiMotion.ReducedMotion ? 1 : .9;
        panel.TranslationY = UiMotion.ReducedMotion ? 0 : 12;
        try
        {
            try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
            catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException) { }
            if (UiMotion.ReducedMotion)
            {
                SemanticProperties.SetDescription(panel, $"{card.Name}. {_revealValue.Text}.");
                await AnimateReveal("Card", generation, 120, p => panel.Opacity = p, Current, operation.Context.Token);
                Check();
                // An animation-owned presentation interval; no business operation waits for a timer.
                await AnimateReveal("Value", generation, 620, _ => { }, Current, operation.Context.Token);
            }
            else
            {
                await AnimateReveal("Card", generation, ScannerRevealMotion.CardMs, p =>
                { panel.Opacity = p; panel.TranslationY = 12 * (1 - p); panel.Scale = .9 + .1 * p; }, Current, operation.Context.Token);
                Check();
                await AnimateReveal("Value", generation, ScannerRevealMotion.ValueMs, p =>
                {
                    _revealValue.Opacity = Math.Min(1, p * 3); _revealSource.Opacity = _revealValue.Opacity;
                    if (presentation.ValueAt(p) is {} amount) _revealValue.Text = Money(amount);
                }, Current, operation.Context.Token);
                Check();
                if (presentation.Value.HasValue)
                {
                    _revealValue.Text = Money(presentation.Value.Value);
                    try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
                    catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException) { }
                    await AnimateReveal("Pop", generation, ScannerRevealMotion.PopMs,
                        p => _revealValue.Scale = 1 + .08 * Math.Sin(Math.PI * p), Current, operation.Context.Token, Easing.Linear);
                    Check();
                    if (_totalDelta is {} delta) { delta.Text = "+ " + Money(presentation.Value.Value); delta.IsVisible = true; delta.Opacity = 1; delta.TranslationY = 0; }
                }
                await AnimateReveal("Total", generation, ScannerRevealMotion.TotalMs,
                    p => _total.Text = Money(presentation.TotalAt(p)), Current, operation.Context.Token);
            }
            Check(); _total.Text = Money(presentation.FinalTotal);
            SemanticProperties.SetDescription(panel, $"{card.Name}. {_revealValue.Text}. Total {Money(presentation.FinalTotal)}.");
            // Fade out the delta after the reveal settles
            if (_totalDelta is {} d && d.IsVisible)
            {
                await AnimateReveal("DeltaFade", generation, ScannerRevealMotion.ExitMs * 2, p =>
                { d.Opacity = 1 - p; d.TranslationY = -8 * p; }, Current, operation.Context.Token);
                d.IsVisible = false;
            }
            await AnimateReveal("Exit", generation, ScannerRevealMotion.ExitMs, p => panel.Opacity = 1 - p, Current, operation.Context.Token);
        }
        finally
        {
            // A cancelled old reveal must never hide or update a replacement session.
            if (generation == _revealGeneration)
            { StopReveal(); if (_visible && _session?.Id == operation.Session.Id && _account.User?.Id == operation.Session.OwnerId) UpdateScore(); }
        }
    }

    private async Task AnimateReveal(string stage, long generation, uint duration, Action<double> update,
        Func<bool> current, CancellationToken token, Easing? easing = null)
    {
        token.ThrowIfCancellationRequested();
        var name = $"ScannerReveal{stage}{generation}";
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = token.Register(() => MainThread.BeginInvokeOnMainThread(() =>
        { this.AbortAnimation(name); completion.TrySetCanceled(token); }));
        new Animation(p => { if (current()) update(p); }).Commit(this, name, ScannerRevealMotion.NumericFrameMs,
            duration, easing ?? Easing.CubicOut, (_, _) => completion.TrySetResult());
        await completion.Task;
        token.ThrowIfCancellationRequested();
    }

    private void StopReveal()
    {
        var generation = _revealGeneration++;
        foreach (var stage in new[] { "Card", "Value", "Pop", "Total", "DeltaFade", "Exit" }) this.AbortAnimation($"ScannerReveal{stage}{generation}");
        _revealPanel?.CancelAnimations(); _revealValue.CancelAnimations();
        if (_revealPanel is {} panel) { panel.IsVisible = false; panel.Scale = 1; panel.TranslationY = 0; }
        _revealValue.Scale = 1;
        if (_totalDelta is {} delta) { delta.IsVisible = false; delta.Opacity = 1; delta.TranslationY = 0; }
        _total.TextColor = TokenColor("TextPrimary"); _total.Scale = 1; _revealInFlight = false;
    }
}