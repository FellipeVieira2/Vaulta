using System.Net;
using System.Net.Http.Json;
using Vaulta.App.Core.Marketplace;
using Vaulta.Marketplace.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Marketplace;

public sealed class MarketplaceProductTests
{
    [Fact]
    public async Task ListingModeDoesNotFetchOffersAndCatalogArtworkUsesApiOrigin()
    {
        var product = Product(); var listing=Offer(product);
        using var handler = new Transport((_,_)=>Task.FromResult(Json(listing)));
        var http=new HttpClient(handler){BaseAddress=new("https://api.test/")};
        using var listingController=new MarketplaceListingDetailController(new MarketplaceClient(http),
            new Vaulta.App.Core.Catalog.CatalogClient(http),loadComparisons:false);
        await listingController.LoadAsync(listing.Id);
        Assert.Single(handler.Requests);
        Assert.False(listingController.State.IsComparing);
        Assert.Equal("https://api.test/api/v1/catalog/printings/artwork", listingController.State.Listing!.Printing!.ArtworkUrl);
        using var catalogTransport=new Transport((_,_)=>Task.FromResult(Json(new Vaulta.Catalog.Contracts.CatalogPrintingDetails(
            product.PrintingId,Guid.NewGuid(),product.SetId,"pokemon","Flames","Charizard","223/197","en",null,"/api/v1/catalog/printings/artwork",[]))));
        var catalog=new Vaulta.App.Core.Catalog.CatalogClient(new HttpClient(catalogTransport){BaseAddress=new("https://api.test/")});
        Assert.Equal("https://api.test/api/v1/catalog/printings/artwork",(await catalog.GetPrintingAsync(product.PrintingId)).ArtworkUrl);
    }

    [Fact]
    public async Task ProductClientUsesExplicitNoneAndResolvesCanonicalArtwork()
    {
        var product = Product();
        using var handler = new Transport((_, _) => Task.FromResult(Json(product)));
        var client = Client(handler);
        var loaded = await client.GetProductAsync(product.PrintingId, null);
        Assert.Equal($"/api/v1/marketplace/products/{product.PrintingId}/variants/none", handler.Requests[0].AbsolutePath);
        Assert.Equal("https://api.test/api/v1/catalog/printings/artwork", loaded!.Printing.ArtworkUrl);
        var shown = MarketplaceProductPresentation.From(loaded);
        Assert.Contains("90,00", shown.LowestPrice);
        Assert.Equal("Cotação indisponível", shown.MarketPrice);
        Assert.Equal("2 ofertas", shown.OfferCount);
    }

    [Fact]
    public async Task BrowseDeduplicatesProductIdentityAndKeepsNullSeparateFromHolo()
    {
        var plain = Product(); var holo = plain with { VariantId = Guid.NewGuid() }; var calls = 0;
        using var handler = new Transport((_, _) => Task.FromResult(Json(new MarketplaceProductPageDto(
            ++calls == 1 ? [plain] : [plain, holo], calls, 20, 2))));
        using var controller = new MarketplaceProductBrowseController(Client(handler));
        await controller.RefreshAsync(); await controller.LoadMoreAsync(); await controller.LoadMoreAsync();
        Assert.Equal(2, controller.State.Items.Count);
        Assert.False(controller.State.HasMore);
        Assert.Contains("/products", handler.Requests[0].AbsolutePath);
        Assert.Contains("page=2", handler.Requests[1].Query);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ReplacedSearchCannotOverwriteNewProduct()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = Product();
        using var handler = new Transport((request, _) => request.RequestUri!.Query.Contains("query=old")
            ? pending.Task : Task.FromResult(Json(new MarketplaceProductPageDto([latest],1,20,1))));
        using var controller = new MarketplaceProductBrowseController(Client(handler));
        var old = controller.RefreshAsync(new("old"));
        await controller.RefreshAsync(new("new"));
        pending.SetResult(Json(new MarketplaceProductPageDto([Product()],1,20,1))); await old;
        Assert.Equal(latest.PrintingId, Assert.Single(controller.State.Items).PrintingId);
        Assert.Equal("new", controller.State.Filters.Query);
    }

    [Fact]
    public async Task FailedOffersKeepProductAndRetryExactIdentity()
    {
        var product = Product(); var offer = Offer(product); var calls = 0;
        using var handler = new Transport((request, _) => Task.FromResult(!request.RequestUri!.AbsolutePath.EndsWith("/offers")
            ? Json(product) : ++calls == 1 ? new(HttpStatusCode.ServiceUnavailable)
            : Json(new ListingPageDto([offer],1,20,1))));
        using var controller = new MarketplaceProductDetailController(Client(handler));
        await controller.LoadAsync(product.PrintingId, null);
        Assert.Equal(product.PrintingId, controller.State.Product?.PrintingId);
        Assert.NotNull(controller.State.OffersMessage);
        await controller.RetryOffersAsync();
        Assert.Equal(offer.Id, Assert.Single(controller.State.Offers).Id);
        Assert.Null(controller.State.OffersMessage);
        Assert.All(handler.Requests.Where(x=>x.AbsolutePath.EndsWith("/offers")), x=>Assert.Contains("/variants/none/offers",x.AbsolutePath));
    }

    [Fact]
    public async Task OfferPaginationRejectsForeignVariantsAndKeepsEarlierPageOnFailure()
    {
        var product = Product(); var offer = Offer(product); var next = offer with { Id = Guid.NewGuid() }; var calls=0;
        using var handler = new Transport((request, _) => Task.FromResult(!request.RequestUri!.AbsolutePath.EndsWith("/offers")
            ? Json(product) : ++calls==1 ? Json(new ListingPageDto([offer],1,1,2))
            : calls==2 ? new(HttpStatusCode.ServiceUnavailable) : Json(new ListingPageDto([next, next with {Id=Guid.NewGuid(),VariantId=Guid.NewGuid()}],2,1,2))));
        using var controller = new MarketplaceProductDetailController(Client(handler));
        await controller.LoadAsync(product.PrintingId,null); await controller.LoadMoreOffersAsync();
        Assert.Equal(offer.Id, Assert.Single(controller.State.Offers).Id);
        await controller.RetryOffersAsync();
        Assert.Equal(new[] {offer.Id,next.Id}, controller.State.Offers.Select(x=>x.Id));
        Assert.False(controller.State.HasMore);
        Assert.Contains("page=2",handler.Requests[^1].Query);
    }

    private static MarketplaceProductClient Client(HttpMessageHandler handler)=>new(new HttpClient(handler){BaseAddress=new("https://api.test/")});
    private static MarketplaceProductDto Product()=>new(Guid.NewGuid(),null,new("Charizard ex","Obsidian Flames","223/197","en","pokemon","/api/v1/catalog/printings/artwork",null),
        Guid.NewGuid(),"SIR",null,90,2,null);
    private static ListingDto Offer(MarketplaceProductDto p)=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),p.PrintingId,p.VariantId,
        "NM",90,"BRL","active",null,[],DateTimeOffset.UtcNow,Guid.NewGuid(),Printing:p.Printing);
    private static HttpResponseMessage Json<T>(T value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
    private sealed class Transport(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> reply):HttpMessageHandler
    {
        public List<Uri> Requests{get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Requests.Add(request.RequestUri!);return reply(request,token);}
    }
}
