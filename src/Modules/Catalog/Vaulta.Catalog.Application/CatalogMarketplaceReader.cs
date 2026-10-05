using Vaulta.Catalog.Contracts;

namespace Vaulta.Catalog.Application;

/// <summary>Local reads only: browsing products never invokes a paid price provider.</summary>
public interface ICatalogMarketplaceReader
{
    Task<IReadOnlyList<Guid>> FindPrintingIds(string? query, string? gameCode, Guid? setId, string? language, CancellationToken ct);
    Task<IReadOnlyList<Guid>> FindVariantIds(string? code, IReadOnlyCollection<Guid> printingIds, CancellationToken ct);
    Task<IReadOnlyList<CatalogMarketQuotes>> GetMarketQuotes(IReadOnlyCollection<Guid> printingIds, CancellationToken ct);
    Task<CatalogMarketplaceFilterOptions> GetFilterOptions(string? gameCode, string? setQuery, int page, int pageSize, CancellationToken ct);
}
public sealed record CatalogMarketQuotes(Guid PrintingId, IReadOnlyList<CardMarketQuoteDto> Quotes, DateTimeOffset RefreshAfter);
public sealed record CatalogMarketplaceSet(Guid Id, string Name);
public sealed record CatalogMarketplaceFilterOptions(IReadOnlyList<CatalogMarketplaceSet> Sets, int Page, int PageSize,
    int TotalCount, IReadOnlyList<string> Languages, IReadOnlyList<string> VariantCodes);
