using Vaulta.App.Core.Marketplace;
using Vaulta.Marketplace.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Marketplace;

public sealed class MarketplaceListingPresentationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SellerPhotoTakesPriorityOverCatalogReference()
    {
        var listing = Listing() with { Photos = [new(Guid.NewGuid(), "front", 0, true, "https://assets.test/front.jpg", Now.AddMinutes(5))] };
        var card = MarketplaceListingPresentation.From(listing, Now);
        Assert.Equal("https://assets.test/front.jpg", card.ImageUrl);
        Assert.False(card.IsCatalogReference);
        Assert.Equal("Foto do vendedor", card.ImageDescription);
    }

    [Fact]
    public void ExpiredSellerPhotoFallsBackToIdentifiedCatalogReference()
    {
        var listing = Listing() with { Photos = [new(Guid.NewGuid(), "front", 0, true, "https://assets.test/front.jpg", Now.AddSeconds(-1))] };
        var card = MarketplaceListingPresentation.From(listing, Now);
        Assert.Equal("https://catalog.test/card.webp", card.ImageUrl);
        Assert.True(card.IsCatalogReference);
        Assert.Equal("Imagem de referência", card.ImageDescription);
    }

    [Fact]
    public void MissingPrintingAndImageAreExplicitAndNoRatingIsInvented()
    {
        var card = MarketplaceListingPresentation.From(Listing() with { Printing = null, SellerAverageRating = 5, SellerTotalReviews = 0 }, Now);
        Assert.Null(card.ImageUrl);
        Assert.Equal("Dados da carta indisponíveis", card.Name);
        Assert.Equal("Sem imagem disponível", card.ImageDescription);
        Assert.Contains("Sem avaliações", card.Seller);
        Assert.DoesNotContain("5,0", card.Seller);
    }

    [Fact]
    public void DisplayUsesRealListingPriceAndAnonymousSellerIdentifier()
    {
        var card = MarketplaceListingPresentation.From(Listing() with { SellerAverageRating = 4.9m, SellerTotalReviews = 18 }, Now);
        Assert.Equal("R$ 289,90", card.Price);
        Assert.Equal("NM · Inglês", card.Metadata);
        Assert.Equal("Vendedor 11111111 · 4,9 (18)", card.Seller);
        Assert.Contains("223/197", card.AccessibleDescription);
    }

    [Fact]
    public void ImageUrlRejectsNonHttpSchemes()
    {
        var listing = Listing() with { Printing = Listing().Printing! with { ArtworkUrl = "file:///private.jpg" } };
        Assert.Null(MarketplaceListingPresentation.From(listing, Now).ImageUrl);
    }

    private static ListingDto Listing() => new(Guid.NewGuid(), Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.NewGuid(), Guid.NewGuid(), null, "NM", 289.90m, "BRL", "active", null, [], Now, Guid.NewGuid(),
        Printing: new("Charizard ex", "Flames", "223/197", "en", "pokemon", "https://catalog.test/card.webp", null));
}
