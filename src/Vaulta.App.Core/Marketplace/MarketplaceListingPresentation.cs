using System.Globalization;
using Vaulta.Marketplace.Contracts;

namespace Vaulta.App.Core.Marketplace;

/// <summary>Truthful public listing content, including legacy responses without catalog metadata.</summary>
public sealed record MarketplaceListingPresentation(Guid Id, string Name, string Price, string Metadata, string Seller,
    string? ImageUrl, bool IsCatalogReference, string ImageDescription, string AccessibleDescription)
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public static MarketplaceListingPresentation From(ListingDto listing, DateTimeOffset now)
    {
        var printing = listing.Printing;
        var name = string.IsNullOrWhiteSpace(printing?.CardName) ? "Dados da carta indisponíveis" : printing.CardName;
        var price = listing.Currency == "BRL" ? listing.PriceBrl.ToString("C", Br) : $"{listing.PriceBrl.ToString("N2", Br)} {listing.Currency}";
        var language = printing?.Language?.ToLowerInvariant() switch
        {
            "en" => "Inglês", "pt" or "pt-br" => "Português", "ja" or "jp" => "Japonês",
            "es" => "Espanhol", "fr" => "Francês", "de" => "Alemão", null or "" => null,
            _ => printing.Language
        };
        var condition = listing.Condition switch { "MINT"=>"M", "NEAR_MINT"=>"NM", "LIGHTLY_PLAYED"=>"LP", "MODERATELY_PLAYED"=>"MP", "HEAVILY_PLAYED"=>"HP", "DAMAGED"=>"DMG", "UNKNOWN"=>"Condição não informada", _=>listing.Condition };
        var metadata = language is null ? condition : $"{condition} · {language}";
        var reputation = listing.SellerTotalReviews > 0
            ? $"{listing.SellerAverageRating.ToString("N1", Br)} ({listing.SellerTotalReviews})" : "Sem avaliações";
        var seller = $"Vendedor {listing.SellerUserId.ToString("N")[..8]} · {reputation}";
        var photo = listing.Photos.Where(photo => photo.UrlExpiresAt > now && IsWebUrl(photo.Url))
            .OrderByDescending(photo => photo.IsPrimary).ThenBy(photo => photo.SortOrder).FirstOrDefault();
        var imageUrl = photo?.Url ?? (IsWebUrl(printing?.ArtworkUrl) ? printing!.ArtworkUrl : null);
        var reference = photo is null && imageUrl is not null;
        var imageDescription = imageUrl is null ? "Sem imagem disponível" : reference ? "Imagem de referência" : "Foto do vendedor";
        var detail = printing is null ? null : $"{printing.SetName}, {printing.CollectorNumber}";
        return new(listing.Id, name, price, metadata, seller, imageUrl, reference, imageDescription,
            $"{name}. {detail}. {price}. {metadata}. {seller}. {imageDescription}. Abrir anúncio.");
    }

    private static bool IsWebUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http";
}
