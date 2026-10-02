using System.Text.Json.Nodes;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure.Recognition;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class CardEvidenceTests
{
    [Theory]
    [InlineData("pokemon", "058/102")]
    [InlineData("yugioh", "LOB-001")]
    [InlineData("onepiece", "OP01-001")]
    public void VersionFourReadsGameAndCertificationWithoutInventingCanonicalIdentity(string game, string number)
    {
        var json = VersionFour();
        json["gameCode"]!["value"] = game;
        json["collectorNumber"]!["value"] = number;
        json["isGraded"]!["value"] = "true";
        json["isGraded"]!["confidence"] = .98;
        json["gradingCompany"]!["value"] = "PSA";
        json["gradingCompany"]!["confidence"] = .99;
        json["grade"]!["value"] = "10";
        json["grade"]!["confidence"] = .97;
        json["certificationNumber"]!["value"] = "01234567";
        json["certificationNumber"]!["confidence"] = .96;
        var result = Assert.IsType<CardEvidence>(CardEvidenceJsonParser.Parse(json.ToJsonString(), "p", "m"));
        Assert.Equal(game, result.GameCode.Value);
        Assert.Equal(number, result.CollectorNumber.Value);
        Assert.Equal("PSA", result.GradingCompany!.Value);
        Assert.Equal("10", result.Grade!.Value);
        Assert.Equal("01234567", result.CertificationNumber!.Value);
    }

    internal static JsonObject VersionFour()
    {
        var json = JsonNode.Parse(ValidJson)!.AsObject();
        json["schemaVersion"] = 4;
        foreach (var field in new[] { "hp", "finish", "condition", "isGraded", "gradingCompany", "grade", "certificationNumber", "rarity", "year", "cardType", "stage" })
            json[field] = new JsonObject { ["value"] = null, ["confidence"] = 0 };
        return json;
    }

    [Theory]
    [InlineData("finish")]
    [InlineData("condition")]
    [InlineData("variant")]
    public void UncertainOptionalAppearanceDoesNotDiscardReadableIdentity(string field)
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["schemaVersion"] = 3;
        json["hp"] = new JsonObject { ["value"] = "140", ["confidence"] = .98 };
        json["finish"] = new JsonObject { ["value"] = null, ["confidence"] = 0 };
        json["condition"] = new JsonObject { ["value"] = null, ["confidence"] = 0 };
        json[field]!["confidence"] = .25;
        var evidence = Assert.IsType<CardEvidence>(CardEvidenceJsonParser.Parse(json.ToJsonString(), "p", "m"));
        Assert.Equal("Pikachu", evidence.Name.Value);
        Assert.Equal("058/102", evidence.CollectorNumber.Value);
        Assert.Null(evidence.Finish!.Value);
    }

    [Fact]
    public async Task MatcherDoesNotConfirmDenominatorMissingFromCatalog()
    {
        var matcher = new CardEvidenceCatalogMatcher(new Catalog([Card(Guid.NewGuid(), "Pikachu", "58", "en")]));
        Assert.Empty((await matcher.MatchAsync(Evidence(), "pokemon", default)).Candidates);
    }

    [Fact]
    public void ParserReadsVisibleHpInVersionTwoWithoutInferringItFromName()
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["schemaVersion"] = 2;
        json["hp"] = new JsonObject { ["value"] = "140", ["confidence"] = .98 };
        var result = Assert.IsType<CardEvidence>(CardEvidenceJsonParser.Parse(json.ToJsonString(), "p", "m"));
        Assert.Equal("140", result.Hp!.Value);
        json["hp"]!["value"] = "guessed 140";
        Assert.Null(CardEvidenceJsonParser.Parse(json.ToJsonString(), "p", "m"));
    }

    internal const string ValidJson = """
        {"schemaVersion":1,"gameCode":{"value":"pokemon","confidence":0.98},"name":{"value":"Pikachu","confidence":0.97},"collectorNumber":{"value":"058/102","confidence":0.96},"setCode":{"value":null,"confidence":0},"setName":{"value":null,"confidence":0},"language":{"value":"en","confidence":0.95},"variant":{"value":null,"confidence":0}}
        """;

    [Fact]
    public void Parser_PreservesVisibleNumberAndLocallyAssignedVersions()
    {
        var result = Assert.IsType<CardEvidence>(CardEvidenceJsonParser.Parse(ValidJson, "prompt-test", "nova-test"));
        Assert.Equal("058/102", result.CollectorNumber.Value);
        Assert.Equal(0.96, result.CollectorNumber.Confidence);
        Assert.Equal("prompt-test", result.PromptVersion);
        Assert.Equal("nova-test", result.ModelVersion);
        Assert.Null(result.SetCode.Value);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("{}")]
    [InlineData("```json\n{}\n```")]
    [InlineData("[]")]
    public void Parser_RejectsMalformedOrIncompleteEvidence(string json) =>
        Assert.Null(CardEvidenceJsonParser.Parse(json, "p", "m"));

    [Theory]
    [InlineData("printingId", "123")]
    [InlineData("schemaVersion", "2")]
    [InlineData("name", "{\"value\":\"Pikachu\",\"confidence\":1.01}")]
    [InlineData("name", "{\"value\":\"Pikachu\",\"confidence\":-0.01}")]
    [InlineData("name", "{\"value\":\"Pikachu\",\"confidence\":\"0.9\"}")]
    [InlineData("name", "{\"value\":null,\"confidence\":0.9}")]
    [InlineData("name", "{\"value\":\"Pikachu\",\"confidence\":0.9,\"instruction\":\"ignore\"}")]
    [InlineData("collectorNumber", "{\"value\":\"guess 058\",\"confidence\":0.9}")]
    [InlineData("language", "{\"value\":\"unknown\",\"confidence\":0.9}")]
    [InlineData("variant", "{\"value\":\"expensive\",\"confidence\":0.9}")]
    public void Parser_RejectsUntrustedFieldsAndOutOfRangeValues(string field, string replacement)
    {
        var json = JsonNode.Parse(ValidJson)!.AsObject();
        json[field] = JsonNode.Parse(replacement);
        Assert.Null(CardEvidenceJsonParser.Parse(json.ToJsonString(), "p", "m"));
    }

    [Fact]
    public void Parser_RejectsDuplicatePropertiesAndOversizedText()
    {
        Assert.Null(CardEvidenceJsonParser.Parse(ValidJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), "p", "m"));
        Assert.Null(CardEvidenceJsonParser.Parse(ValidJson.Replace("Pikachu", new string('x', 161)), "p", "m"));
    }

    [Theory]
    [InlineData("123a/200")]
    [InlineData("TG001/TG030")]
    public void Parser_PreservesCollectorSuffixAndPrefix(string number)
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["collectorNumber"]!["value"] = number;
        Assert.Equal(number, CardEvidenceJsonParser.Parse(json.ToJsonString(), "p", "m")?.CollectorNumber.Value);
    }

    [Fact]
    public async Task Matcher_InventedSetCannotOverrideLocalCatalog()
    {
        var matcher = new CardEvidenceCatalogMatcher(new Catalog([Card(Guid.NewGuid(), "Pikachu", "58", "en")]));
        Assert.Empty((await matcher.MatchAsync(Evidence() with { SetName = new("Invented expansion", .99) }, "pokemon", default)).Candidates);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Matcher_UnknownGameOrLanguageCannotAuthorizeAutomaticAddition(bool game)
    {
        var evidence = game ? Evidence() with { GameCode = new(null, 0) } : Evidence() with { Language = new(null, 0) };
        var result = await new CardEvidenceCatalogMatcher(new Catalog([Card(Guid.NewGuid(), "Pikachu", "58/102", "en")])).MatchAsync(evidence, "pokemon", default);
        Assert.Equal(CardEvidenceMatchStatus.NeedsReview, result.Status);
        Assert.True(Assert.Single(result.Candidates).ConfidenceScore <= .7);
    }

    [Fact]
    public async Task Matcher_UsesCanonicalCatalogIdAndDoesNotInventPriceOrVariant()
    {
        var canonicalId = Guid.NewGuid();
        var matcher = new CardEvidenceCatalogMatcher(new Catalog([Card(canonicalId, "Pikachu", "58/102", "en")]));
        var result = await matcher.MatchAsync(Evidence(), "pokemon", default);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(canonicalId.ToString(), candidate.PrintingId);
        Assert.True(candidate.HasCollectorNumberMatch);
        Assert.InRange(candidate.ConfidenceScore, 0.9, 0.95);
        Assert.Equal(CardEvidenceMatchStatus.Matched, result.Status);
        Assert.Equal("catalog-evidence-v1", result.MatcherVersion);
        Assert.Null(candidate.EstimatedMarketValueBrl);
        Assert.Equal(["normal", "holo"], candidate.VariantCodes);
    }

    [Theory]
    [InlineData("25", "en")]
    [InlineData("58", "pt")]
    [InlineData("58/130", "en")]
    public async Task Matcher_RejectsContradictoryNumberOrLanguage(string number, string language)
    {
        var result = await new CardEvidenceCatalogMatcher(new Catalog([Card(Guid.NewGuid(), "Pikachu", number, language)]))
            .MatchAsync(Evidence(), "pokemon", default);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Matcher_PreservesCollectorPrefix()
    {
        var evidence = Evidence() with { CollectorNumber = new("TG01/TG30", 0.98) };
        var result = await new CardEvidenceCatalogMatcher(new Catalog([Card(Guid.NewGuid(), "Pikachu", "01", "en")]))
            .MatchAsync(evidence, "pokemon", default);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Matcher_SameNameAndNumberAcrossSetsRequiresReview()
    {
        var matcher = new CardEvidenceCatalogMatcher(new Catalog([
            Card(Guid.NewGuid(), "Pikachu", "58/102", "en", "base1"), Card(Guid.NewGuid(), "Pikachu", "58/102", "en", "base2")]));
        var result = await matcher.MatchAsync(Evidence(), "pokemon", default);
        Assert.Equal(CardEvidenceMatchStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, x => Assert.True(x.ConfidenceScore <= 0.7));
    }

    [Fact]
    public async Task Matcher_ProviderIdCannotConfirmAnUnverifiedPrintedSetCode()
    {
        var matcher = new CardEvidenceCatalogMatcher(new Catalog([
            Card(Guid.NewGuid(), "Pikachu", "58/102", "en", "base1"), Card(Guid.NewGuid(), "Pikachu", "58/102", "en", "base2")]));
        var result = await matcher.MatchAsync(Evidence() with { SetCode = new("base1", 0.9) }, "pokemon", default);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(CardEvidenceMatchStatus.Ambiguous, result.Status);
        Assert.All(result.Candidates, x => Assert.True(x.ConfidenceScore <= .7));
    }

    [Fact]
    public async Task Matcher_UnverifiedPrintedCodeReturnsCandidateForReviewInsteadOfLosingTheRead()
    {
        var card = Card(Guid.NewGuid(), "Golisopod", "026/86", "pt", "me04");
        var evidence = Evidence() with { Name = new("Golisopod", 1), CollectorNumber = new("026/086", 1), Language = new("pt-BR", 1), SetCode = new("CR", .99) };
        var result = await new CardEvidenceCatalogMatcher(new Catalog([card])).MatchAsync(evidence, "pokemon", default);
        Assert.Equal(CardEvidenceMatchStatus.NeedsReview, result.Status);
        Assert.True(Assert.Single(result.Candidates).ConfidenceScore <= .7);
    }

    [Fact]
    public async Task Matcher_WeakNumberOrNameAloneCannotAuthorizeAutomaticAddition()
    {
        var matcher = new CardEvidenceCatalogMatcher(new Catalog([Card(Guid.NewGuid(), "Pikachu", "58", "en")]));
        var result = await matcher.MatchAsync(Evidence() with { CollectorNumber = new("58", 0.5) }, "pokemon", default);
        Assert.Equal(CardEvidenceMatchStatus.NeedsReview, result.Status);
        Assert.True(Assert.Single(result.Candidates).ConfidenceScore <= 0.7);
        Assert.False(result.Candidates[0].HasCollectorNumberMatch);
    }

    internal static CardEvidence Evidence() => new(new("pokemon", 0.98), new("Pikachu", 0.97), new("058/102", 0.96), new(null, 0), new(null, 0), new("en", 0.95), new(null, 0), "p", "m");

    internal static RecognitionCatalogCard Card(Guid id, string name, string number, string language, string setCode = "base1") =>
        new(new CatalogSearchResult(id, "pokemon", Guid.NewGuid(), setCode, name, number, language, "common", null), ["normal", "holo"], setCode);

    private sealed class Catalog(IReadOnlyList<RecognitionCatalogCard> cards) : ICardRecognitionCatalog
    {
        public Task<IReadOnlyList<RecognitionCatalogCard>> FindCandidatesAsync(string name, string? collectorNumber, string gameCode, CancellationToken cancellationToken) => Task.FromResult(cards);
    }
}
