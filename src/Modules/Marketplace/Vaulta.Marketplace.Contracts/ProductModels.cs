namespace Vaulta.Marketplace.Contracts;

public sealed record BrowseProductsQueryDto(string? Query = null, string? GameCode = null, int Page = 1, int PageSize = 20,
    string Sort = "newest", Guid? SetId = null, string? Language = null, string? VariantCode = null,
    string? Condition = null, decimal? MinPriceBrl = null, decimal? MaxPriceBrl = null, bool PhotosOnly = false);
public sealed record MarketplaceMarketQuoteDto(decimal MarketValueBrl, string Source, DateTimeOffset UpdatedAt,
    decimal OriginalValue, string OriginalCurrency, decimal ExchangeRate, DateTimeOffset ExchangeRateAt,
    DateTimeOffset? NextRefreshAt);
public sealed record MarketplaceProductDto(Guid PrintingId, Guid? VariantId, ListingPrintingDto Printing,
    Guid SetId, string? Rarity, string? VariantName, decimal? LowestPriceBrl, int OfferCount, MarketplaceMarketQuoteDto? MarketQuote);
public sealed record MarketplaceProductPageDto(IReadOnlyList<MarketplaceProductDto> Items, int Page, int PageSize, int TotalCount);
public sealed record MarketplaceSetOptionDto(Guid Id, string Name);
public sealed record MarketplaceProductFilterOptionsDto(IReadOnlyList<MarketplaceSetOptionDto> Sets, int SetPage,
    int SetPageSize, int SetTotalCount, IReadOnlyList<string> Languages, IReadOnlyList<string> VariantCodes);
