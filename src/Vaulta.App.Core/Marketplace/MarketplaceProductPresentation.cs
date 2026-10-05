using System.Globalization;
using Vaulta.Marketplace.Contracts;
namespace Vaulta.App.Core.Marketplace;

public sealed record MarketplaceProductPresentation(Guid PrintingId, Guid? VariantId, string Name, string Identity, string Finish,
    string LowestPrice, string MarketPrice, string OfferCount, string? ArtworkUrl, string MarketSource, string AccessibleDescription)
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    public static MarketplaceProductPresentation From(MarketplaceProductDto product)
    {
        var p = product.Printing;
        var language = p.Language.ToLowerInvariant() switch { "en"=>"Inglês", "pt" or "pt-br"=>"Português", "ja" or "jp"=>"Japonês", _=>p.Language };
        var identity = $"{p.SetName} · {p.CollectorNumber} · {language}";
        var finish = product.VariantName ?? p.VariantCode ?? "Acabamento não declarado";
        var lowest = product.OfferCount > 0 && product.LowestPriceBrl.HasValue ? product.LowestPriceBrl.Value.ToString("C",Br) : "Sem ofertas";
        var market = product.MarketQuote?.MarketValueBrl.ToString("C",Br) ?? "Cotação indisponível";
        var count = product.OfferCount == 0 ? "Sem ofertas" : $"{product.OfferCount} oferta{(product.OfferCount == 1 ? "" : "s")}";
        var source = product.MarketQuote is { } q ? $"{q.Source} · {q.UpdatedAt.ToLocalTime():dd/MM/yyyy}" +
            (q.NextRefreshAt <= DateTimeOffset.UtcNow ? " · atualização pendente" : "") : "Referência de mercado ainda não disponível";
        return new(product.PrintingId,product.VariantId,p.CardName,identity,finish,lowest,market,count,p.ArtworkUrl,source,
            $"{p.CardName}. {identity}. {finish}. A partir de {lowest}. Mercado: {market}. {count}. Abrir ofertas desta impressão.");
    }
}
