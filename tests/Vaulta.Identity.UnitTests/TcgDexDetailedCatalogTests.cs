using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Infrastructure;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class TcgDexDetailedCatalogTests
{
    [Fact]
    public async Task DetailedTreatmentsPreserveEditionStampAndMetadataWithoutInventingVariants()
    {
        using var client = new HttpClient(new Boundary()) { BaseAddress = new("https://api.tcgdex.net/v2/") };
        var provider = new TcgDexProvider(client, Options.Create(new TcgDexOptions()));
        var result = await provider.GetSetDetails("base1", default);
        var card = Assert.Single(result.Printings);
        Assert.Single(card.Variants);
        Assert.Contains("first-edition", card.Variants[0].Code);
        using var raw = JsonDocument.Parse(card.Variants[0].RawValue);
        Assert.Equal("holo", raw.RootElement.GetProperty("type").GetString());
        Assert.Equal("unlimited", raw.RootElement.GetProperty("subtype").GetString());
        Assert.Equal("1st-edition", raw.RootElement.GetProperty("stamp")[0].GetString());
    }
    [Fact]
    public async Task ProviderStoresSeriesAndEvidenceNeededToResolvePrintings()
    {
        using var client = new HttpClient(new Boundary()) { BaseAddress = new("https://api.tcgdex.net/v2/") };
        var result = await new TcgDexProvider(client, Options.Create(new TcgDexOptions())).GetSetDetails("base1", default);
        Assert.NotNull(result.Set.GetType().GetProperty("Series"));
        var metadataProperty = Assert.Single(result.Printings).GetType().GetProperty("MetadataJson");
        Assert.NotNull(metadataProperty);
        using var metadata = JsonDocument.Parse((string)metadataProperty.GetValue(result.Printings[0])!);
        Assert.Equal(120, metadata.RootElement.GetProperty("hp").GetInt32());
        Assert.Equal("Mitsuhiro Arita", metadata.RootElement.GetProperty("illustrator").GetString());
    }
    private sealed class Boundary : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var set = request.RequestUri!.AbsolutePath.Contains("/sets/", StringComparison.Ordinal);
            var body = set ? """{"id":"base1","name":"Base Set","releaseDate":"1999-01-09","serie":{"id":"base","name":"Base"},"cards":[{"id":"base1-4","name":"Charizard","localId":"4"}]}"""
                : """{"id":"base1-4","name":"Charizard","localId":"4","hp":120,"illustrator":"Mitsuhiro Arita","category":"Pokemon","types":["Fire"],"set":{"id":"base1","name":"Base Set","cardCount":{"official":102}},"variants":{"normal":true,"holo":true,"reverse":true},"variants_detailed":[{"type":"holo","subtype":"unlimited","stamp":["1st-edition"],"foil":"cosmos","size":"standard"}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    [Theory][InlineData("Normal","Padrão")][InlineData("Normale","Standard")][InlineData("normal","standard")]
    public void StandardSizesAndLocalizedTypeShareCanonicalSurfaceCode(string type,string size)
    {
        using var raw=JsonDocument.Parse(JsonSerializer.Serialize(new {type,size}));
        Assert.Equal("normal",TcgDexProvider.DetailedVariant(raw.RootElement).Code);
    }

}