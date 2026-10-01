using Vaulta.Catalog.Contracts;
using Vaulta.Collection.Contracts;
using Vaulta.SharedKernel;

namespace Vaulta.Collection.Application;

public sealed record CollectionValuationPosition(Guid EntryId, Guid PrintingId, Guid? VariantId, int Quantity);

public interface ICollectionValuationSource
{
    // Positions contain every active copy, including listed copies, but never sold/deleted history.
    Task<IReadOnlyList<CollectionValuationPosition>> GetPositions(Guid owner, Guid? entryId, CancellationToken ct);
    Task<IReadOnlyList<CardMarketQuoteDto>> GetQuotes(Guid printingId, CancellationToken ct);
}

public sealed record CollectionValuationLimits(TimeSpan RefreshBudget, TimeSpan QuoteTimeout)
{
    public static CollectionValuationLimits Default { get; } = new(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(4));
}

public sealed class CollectionValuationService(ICollectionValuationSource source, IClock clock, CollectionValuationLimits limits)
{
    public async Task<CollectionValuationDto> Get(Guid owner, Guid? entryId, CancellationToken ct)
    {
        if (owner == Guid.Empty || entryId == Guid.Empty) throw new DomainException("Invalid collection owner or entry.");
        var positions = await source.GetPositions(owner, entryId, ct);
        decimal total = 0; var priced = 0; DateTimeOffset? oldest = null; var incomplete = false;
        var comparisons = new Dictionary<int, (decimal Average, decimal Current, int Items)>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(limits.RefreshBudget);
        // A printing is fetched once even when its physical copies are split across variants/conditions.
        // Calls are sequential because the Catalog reader shares a scoped EF context.
        foreach (var printing in positions.GroupBy(x => x.PrintingId))
        {
            ct.ThrowIfCancellationRequested();
            if (budget.IsCancellationRequested) { incomplete = true; continue; }
            IReadOnlyList<CardMarketQuoteDto> quotes;
            using var quoteTimeout = CancellationTokenSource.CreateLinkedTokenSource(budget.Token); quoteTimeout.CancelAfter(limits.QuoteTimeout);
            try { quotes = await source.GetQuotes(printing.Key, quoteTimeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && quoteTimeout.IsCancellationRequested) { incomplete = true; continue; }
            foreach (var position in printing)
            {
                if (position.VariantId is null) continue; // no price guessed for an unidentified variant
                var quote = quotes.Where(x => x.VariantId == position.VariantId && IsValid(x))
                    .OrderBy(x => x.Source.Contains("TCGplayer", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenByDescending(x => x.UpdatedAt).FirstOrDefault();
                if (quote is null) continue;
                total += quote.MarketValueBrl * position.Quantity; priced += position.Quantity;
                oldest = oldest is null || quote.UpdatedAt < oldest ? quote.UpdatedAt : oldest;
                foreach (var comparison in quote.Comparisons.Where(x => x.Days is 1 or 7 or 30)
                    .Where(x => x.AverageBrl >= 0 && decimal.Round(x.AverageBrl, 2) == x.AverageBrl).DistinctBy(x => x.Days))
                {
                    var prior = comparisons.GetValueOrDefault(comparison.Days);
                    comparisons[comparison.Days] = (prior.Average + comparison.AverageBrl * position.Quantity,
                        prior.Current + quote.MarketValueBrl * position.Quantity, prior.Items + position.Quantity);
                }
            }
        }
        ct.ThrowIfCancellationRequested();
        var periods = comparisons.OrderBy(x => x.Key).Select(x => new CollectionMarketComparisonDto(x.Key, x.Value.Average,
            x.Value.Current, x.Value.Current - x.Value.Average,
            x.Value.Average > 0 ? decimal.Round((x.Value.Current - x.Value.Average) / x.Value.Average * 100, 2) : null, x.Value.Items)).ToArray();
        var items = positions.Sum(x => x.Quantity);
        return new("BRL", total, items, priced, items - priced, positions.Count, incomplete, clock.UtcNow, oldest, periods,
            "Referências internacionais convertidas para reais pela PTAX, sem ajuste pela condição. Comparações usam as médias do período das cartas atuais, não o histórico de saldo nem lucro realizado."
            + (incomplete ? " Algumas consultas não terminaram. Atualize para continuar a avaliação." : ""));
    }

    private static bool IsValid(CardMarketQuoteDto quote) => quote.MarketValueBrl >= 0 && decimal.Round(quote.MarketValueBrl, 2) == quote.MarketValueBrl
        && !string.IsNullOrWhiteSpace(quote.Source) && quote.UpdatedAt != default && quote.ExchangeRateAt != default
        && quote.OriginalCurrency is "USD" or "EUR" or "BRL" && quote.OriginalValue >= 0 && quote.ExchangeRate > 0
        && decimal.Round(quote.OriginalValue * quote.ExchangeRate, 2, MidpointRounding.AwayFromZero) == quote.MarketValueBrl;
}
