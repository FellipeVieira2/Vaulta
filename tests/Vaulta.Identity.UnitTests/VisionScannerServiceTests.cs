using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Xunit;
namespace Vaulta.Identity.UnitTests;
public sealed class VisionScannerServiceTests
{
    [Theory] [InlineData(true,"available")] [InlineData(false,"unavailable")]
    public async Task IdentitySurvivesMissingPriceAndQuoteBelongsToResolvedVariant(bool priced,string priceStatus)
    {
        var card=VisionPrintingResolverTests.Printing("067/086","pt-BR"); var sequence=new List<string>(); var encoder=new Encoder(sequence); var catalog=new Catalog(card); var quotes=new Quotes(card,priced);
        var service=new VisionScannerService(encoder,new Index(encoder.Identity,card),catalog,new Evidence(sequence,VisionPrintingResolverTests.Evidence("067/086")),quotes,new());
        var result=await service.IdentifyAsync(new([new([1,2,3])]),default);
        Assert.Equal("identified",result.Status); Assert.Equal(card.Printing.PrintingId,result.PrintingId); Assert.Equal(priceStatus,result.PriceStatus);
        Assert.Equal(new[]{"encoder","evidence"},sequence); Assert.Equal(1,quotes.Calls); Assert.True(catalog.EvidenceLookups>0);
        if(priced) { Assert.Equal(25m,result.Price!.MarketValueBrl); Assert.Equal(result.VariantId,result.Price.VariantId); } else Assert.Null(result.Price);
    }
    [Theory] [InlineData("back","true","back")] [InlineData(null,"false","not_a_card")]
    public async Task BackAndNonCardNeverSelectPrintingOrQuote(string? side,string isCard,string status)
    {
        var card=VisionPrintingResolverTests.Printing("067/086","pt-BR"); var sequence=new List<string>(); var encoder=new Encoder(sequence); var quotes=new Quotes(card,true);
        var e=VisionPrintingResolverTests.Evidence("067/086") with { CardSide=new(side,.96),IsCard=new(isCard,.96) };
        var result=await new VisionScannerService(encoder,new Index(encoder.Identity,card),new Catalog(card),new Evidence(sequence,e),quotes,new()).IdentifyAsync(new([new([1])]),default);
        Assert.Equal(status,result.Status); Assert.Null(result.PrintingId); Assert.Null(result.Price); Assert.Equal(0,quotes.Calls);
    }
    [Fact] public async Task GradedCardDoesNotBorrowAnUngradedPrice()
    {
        var card=VisionPrintingResolverTests.Printing("067/086","pt-BR"); var sequence=new List<string>(); var encoder=new Encoder(sequence); var quotes=new Quotes(card,true);
        var e=VisionPrintingResolverTests.Evidence("067/086") with { IsGraded=new("true",.97),GradingCompany=new("PSA",.97),Grade=new("10",.97) };
        var result=await new VisionScannerService(encoder,new Index(encoder.Identity,card),new Catalog(card),new Evidence(sequence,e),quotes,new()).IdentifyAsync(new([new([1])]),default);
        Assert.Equal("identified",result.Status); Assert.Null(result.Price); Assert.Equal("graded_unavailable",result.PriceStatus); Assert.Equal(0,quotes.Calls);
    }
    // Artificial encoder is an orchestration fixture, never proof of visual inference.
    private sealed class Encoder(List<string> calls) : IImageEncoder
    {
        public EncoderIdentity Identity { get; }=new("fixture","fixture","fixture","fixture",3,"test","input","output");
        public Task<ImageEmbedding> EncodeAsync(Stream stream,CancellationToken ct) { calls.Add("encoder"); return Task.FromResult(new ImageEmbedding(Identity,[1f,0f,0f])); }
    }
    private sealed class Index(EncoderIdentity identity,VisionCatalogPrinting card) : IVisualReferenceIndex
    {
        public VisualIndexStatus Status=>new("test",1,identity);
        public Task<IReadOnlyList<VisualMatch>> SearchAsync(ImageEmbedding embedding,int topK,CancellationToken ct)=>Task.FromResult<IReadOnlyList<VisualMatch>>([new(Guid.NewGuid(),card.Printing.PrintingId,.99,"fixture")]);
    }
    private sealed class Catalog(VisionCatalogPrinting card) : IVisionCatalog
    {
        public int EvidenceLookups;
        public Task<IReadOnlyList<VisionCatalogPrinting>> GetPrintingsAsync(IReadOnlyList<Guid> ids,CancellationToken ct)=>Task.FromResult<IReadOnlyList<VisionCatalogPrinting>>([card]);
        public Task<IReadOnlyList<VisionCatalogPrinting>> FindEvidenceCandidatesAsync(CardEvidence e,CancellationToken ct) { EvidenceLookups++; return Task.FromResult<IReadOnlyList<VisionCatalogPrinting>>([card]); }
    }
    private sealed class Evidence(List<string> calls,CardEvidence e) : IVisionEvidenceReader
    { public Task<VisionEvidenceReading> ReadAsync(byte[] image,IReadOnlyList<VisionCatalogPrinting> candidates,CancellationToken ct) { calls.Add("evidence"); return Task.FromResult(new VisionEvidenceReading(e,null,null)); } }
    private sealed class Quotes(VisionCatalogPrinting card,bool priced) : IScannerCardDetailsReader
    {
        public int Calls;
        public Task<ScannerCardDetailsDto?> GetAsync(Guid id,CancellationToken ct)
        {
            Calls++; Assert.Equal(card.Printing.PrintingId,id);
            CardMarketQuoteDto quote=new(card.Variants[0].Id,"Normal",25m,5m,"USD","fixture",DateTimeOffset.UtcNow,5m,DateTimeOffset.UtcNow,[]);
            return Task.FromResult<ScannerCardDetailsDto?>(new(card.Printing,new Dictionary<string,string>(),priced?[quote]:[],null));
        }
    }
}
