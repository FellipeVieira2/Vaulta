using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Marketplace.Application;
using Vaulta.Marketplace.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Marketplace.Infrastructure;

public sealed class MarketplaceProductQueries(MarketplaceDbContext db, IMarketplaceCatalog catalog,
    ICatalogMarketplaceReader marketCatalog, IMarketplaceQueries listings) : IMarketplaceProductQueries
{
    private IQueryable<Listing> Active() => db.Listings.AsNoTracking().Where(x => x.Status == MarketplaceRules.ActiveStatus && x.Currency == "BRL"
        && db.SellerProfiles.Any(s => s.UserId == x.SellerUserId && s.Status == MarketplaceRules.ActiveStatus));

    public async Task<MarketplaceProductPageDto> BrowseProducts(BrowseProductsQueryDto request, CancellationToken ct)
    {
        Validate(request);
        var paging = Pagination.Normalize(request.Page, request.PageSize);
        var printingIds = await marketCatalog.FindPrintingIds(request.Query, request.GameCode, request.SetId, request.Language, ct);
        if (printingIds.Count == 0) return new([], paging.Page, paging.Size, 0);
        var query = Active().Where(x => printingIds.Contains(x.PrintingId));
        var variantCode = string.IsNullOrWhiteSpace(request.VariantCode) ? null : request.VariantCode.Trim().ToLowerInvariant();
        var variants = await marketCatalog.FindVariantIds(variantCode, printingIds, ct);
        query = variantCode is null
            ? query.Where(x => !x.VariantId.HasValue || variants.Contains(x.VariantId.Value))
            : query.Where(x => x.VariantId.HasValue && variants.Contains(x.VariantId.Value));
        if (!string.IsNullOrWhiteSpace(request.Condition))
        {
            var condition = request.Condition.Trim().ToUpperInvariant();
            var alias = condition switch { "MINT" => "M", "NEAR_MINT" => "NM", "LIGHTLY_PLAYED" => "LP",
                "MODERATELY_PLAYED" => "MP", "HEAVILY_PLAYED" => "HP", "DAMAGED" => "DMG", _ => condition };
            query = query.Where(x => x.Condition == condition || x.Condition == alias);
        }
        if (request.MinPriceBrl.HasValue) query = query.Where(x => x.PriceBrl >= request.MinPriceBrl.Value);
        if (request.MaxPriceBrl.HasValue) query = query.Where(x => x.PriceBrl <= request.MaxPriceBrl.Value);
        if (request.PhotosOnly) query = query.Where(x => x.Photos.Any());
        var grouped = query.GroupBy(x => new { x.PrintingId, x.VariantId }).Select(g => new Aggregate
        {
            PrintingId = g.Key.PrintingId, VariantId = g.Key.VariantId, Minimum = g.Min(x => x.PriceBrl),
            Count = g.Count(), Latest = g.Max(x => x.CreatedAt)
        });
        var total = await grouped.CountAsync(ct);
        var sorted = request.Sort switch
        {
            "price_asc" => grouped.OrderBy(x => x.Minimum).ThenBy(x => x.PrintingId).ThenBy(x => x.VariantId),
            "price_desc" => grouped.OrderByDescending(x => x.Minimum).ThenBy(x => x.PrintingId).ThenBy(x => x.VariantId),
            _ => grouped.OrderByDescending(x => x.Latest).ThenBy(x => x.PrintingId).ThenBy(x => x.VariantId)
        };
        var page = await sorted.Skip(paging.Offset).Take(paging.Size).ToArrayAsync(ct);
        var ids = page.Select(x => x.PrintingId).Distinct().ToArray();
        var metadata = (await catalog.GetPrintings(ids, ct)).ToDictionary(x => x.PrintingId);
        var variantIds = page.Where(x => x.VariantId.HasValue).Select(x => x.VariantId!.Value).Distinct().ToArray();
        var finishes = (await catalog.GetVariants(variantIds, ct)).ToDictionary(x => x.VariantId);
        var quotes = (await marketCatalog.GetMarketQuotes(ids, ct)).ToDictionary(x => x.PrintingId);
        var products = page.Where(x => metadata.ContainsKey(x.PrintingId)).Select(x =>
            Map(metadata[x.PrintingId], x.VariantId, finishes.GetValueOrDefault(x.VariantId ?? Guid.Empty), x.Minimum, x.Count, quotes.GetValueOrDefault(x.PrintingId))).ToArray();
        return new(products, paging.Page, paging.Size, total);
    }
    public async Task<MarketplaceProductDto?> GetProduct(Guid printingId, Guid? variantId, CancellationToken ct)
    {
        var printing = await catalog.GetPrinting(printingId, ct);
        if (printing is not { IsActive: true }) return null;
        var variant = variantId.HasValue ? await catalog.GetVariant(variantId.Value, ct) : null;
        if (variantId.HasValue && (variant is not { IsActive: true } || variant.PrintingId != printingId)) return null;
        var aggregate = await Active().Where(x => x.PrintingId == printingId && x.VariantId == variantId)
            .GroupBy(x => x.PrintingId).Select(g => new { Minimum = g.Min(x => x.PriceBrl), Count = g.Count() }).SingleOrDefaultAsync(ct);
        var quotes = await marketCatalog.GetMarketQuotes([printingId], ct);
        return Map(printing, variantId, variant, aggregate?.Minimum, aggregate?.Count ?? 0, quotes.FirstOrDefault());
    }
    public async Task<ListingPageDto?> GetOffers(Guid printingId, Guid? variantId, int page, int pageSize, string sort, CancellationToken ct)
    {
        var printing = await catalog.GetPrinting(printingId, ct);
        if (printing is not { IsActive: true }) return null;
        if (variantId.HasValue)
        {
            var variant = await catalog.GetVariant(variantId.Value, ct);
            if (variant is not { IsActive: true } || variant.PrintingId != printingId) return null;
        }
        return await listings.BrowseListings(new(null, printingId, variantId, null, null, page, pageSize, sort, ExactVariant: true), ct);
    }
    private static MarketplaceProductDto Map(CollectionPrintingDetails printing, Guid? variantId, CollectionVariantDetails? variant,
        decimal? minimum, int count, CatalogMarketQuotes? snapshot)
    {
        var valid = variant?.PrintingId == printing.PrintingId && variant.IsActive;
        var quote = valid ? snapshot?.Quotes.Where(x => x.VariantId == variantId).OrderByDescending(x => x.UpdatedAt).FirstOrDefault() : null;
        return new(printing.PrintingId, variantId, new(printing.CardName, printing.SetName, printing.CollectorNumber, printing.Language,
            printing.GameCode, printing.ArtworkUrl, valid ? variant!.Code : null), printing.SetId, printing.Rarity, valid ? variant!.Name : null,
            minimum, count, quote is null ? null : new(quote.MarketValueBrl, quote.Source, quote.UpdatedAt, quote.OriginalValue,
                quote.OriginalCurrency, quote.ExchangeRate, quote.ExchangeRateAt, snapshot!.RefreshAfter));
    }
    private static void Validate(BrowseProductsQueryDto request)
    {
        if (request.MinPriceBrl < 0 || request.MaxPriceBrl < 0 || request.MinPriceBrl > request.MaxPriceBrl) throw new DomainException("Invalid BRL price range.");
        if (!string.IsNullOrWhiteSpace(request.Condition)) MarketplaceRules.DraftCondition(request.Condition);
        if (request.Query?.Length > 160 || request.Language?.Length > 16 || request.VariantCode?.Length > 64) throw new DomainException("Marketplace filter is too long.");
        if (request.Sort is not ("newest" or "price_asc" or "price_desc")) throw new DomainException("Unsupported product ordering.");
    }
    private sealed class Aggregate
    {
        public Guid PrintingId { get; init; }
        public Guid? VariantId { get; init; }
        public decimal Minimum { get; init; }
        public int Count { get; init; }
        public DateTimeOffset Latest { get; init; }
    }
}
