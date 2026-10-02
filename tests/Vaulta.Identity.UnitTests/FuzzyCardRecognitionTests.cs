using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.Catalog.Infrastructure.Recognition;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class FuzzyCardRecognitionTests
{
    [Fact]
    public async Task Recognition_NameWithoutNumberDoesNotAuthorizeAutomaticAddition()
    {
        var service = new FuzzyCardSearchService(new FakeRecognitionCatalog("Professor Oak", "88"), NullLogger<FuzzyCardSearchService>.Instance);
        var match = Assert.Single(await service.SearchAsync(new("Professor Oak", []), "pokemon", CancellationToken.None));
        Assert.False(match.HasCollectorNumberMatch);
        Assert.True(match.ConfidenceScore <= 0.7);
    }

    [Fact]
    public async Task Recognition_CharmeleonPromoNumberMustNeverResolveToBaseSetTwentyFour()
    {
        var service = new FuzzyCardSearchService(new FakeRecognitionCatalog("Charmeleon", "24"), NullLogger<FuzzyCardSearchService>.Instance);
        var matches = await service.SearchAsync(new("Charmeleon\nHálito de Fogo Constante\n079/100", []), "pokemon", default);
        Assert.Empty(matches);
    }

    [Fact]
    public async Task Recognition_RecoversBReadInsteadOfEightInNumericCollectorNumber()
    {
        var service = new FuzzyCardSearchService(new FakeRecognitionCatalog("Professor Oak", "88"), NullLogger<FuzzyCardSearchService>.Instance);
        var result = await service.SearchAsync(new("Professor Oak\nDiscard your hand\nB8/102", []), "pokemon", CancellationToken.None);
        Assert.Single(result);
        Assert.True(result[0].HasCollectorNumberMatch);
    }

    [Fact]
    public async Task Recognition_PreservesValidSingleLetterCollectorPrefix()
    {
        var catalog = new FakeRecognitionCatalog("Professor Oak", "B8");
        var service = new FuzzyCardSearchService(catalog, NullLogger<FuzzyCardSearchService>.Instance);
        var result = await service.SearchAsync(new("Professor Oak\nB8/102", []), "pokemon", CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("B8/102", catalog.Number);
    }

    [Fact]
    public async Task Recognition_TriesNameAfterMisreadDecorativeHeader()
    {
        var catalog = new FakeRecognitionCatalog("Professor Oak", "88");
        var service = new FuzzyCardSearchService(catalog, NullLogger<FuzzyCardSearchService>.Instance);
        var result = await service.SearchAsync(new("TRA1NER\nProfessor Oak\nDiscard your hand\n88/102", []), "pokemon", CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("Professor Oak", catalog.Name);
    }

    [Fact]
    public async Task Recognition_HandlesTrainerHeaderAndAlphanumericCollectorNumber()
    {
        var catalog = new FakeRecognitionCatalog("Professor Oak", "TG01");
        var service = new FuzzyCardSearchService(catalog, NullLogger<FuzzyCardSearchService>.Instance);
        var result = await service.SearchAsync(new("TRAINER\nSupporter\nProfessor Oak\nDiscard your hand\nTG 01 / TG 30", []), "pokemon", CancellationToken.None);
        Assert.Equal("Professor Oak", catalog.Name);
        Assert.Single(result);
    }

    [Fact]
    public async Task Recognition_SkipsEvolutionInstructionsBeforeName()
    {
        var catalog = new FakeRecognitionCatalog("Charizard", "4");
        var service = new FuzzyCardSearchService(catalog, NullLogger<FuzzyCardSearchService>.Instance);
        var result = await service.SearchAsync(new("STAGE 2\nEvolves from Charmeleon\nPut Charizard on the Stage 1 card\n. Charizard\n120 HP\n4/102", []), "pokemon", CancellationToken.None);
        Assert.Equal("Charizard", catalog.Name);
        Assert.Single(result);
    }

    [Fact]
    public async Task Recognition_SkipsBasicPokemonLabelAboveName()
    {
        var catalog = new FakeRecognitionCatalog("Pikachu", "58");
        var service = new FuzzyCardSearchService(catalog, NullLogger<FuzzyCardSearchService>.Instance);
        var result = await service.SearchAsync(new("Basic Pokémon\nPikachu\n40 HP\nGnaw\n58/102", []), "pokemon", CancellationToken.None);
        Assert.Equal("Pikachu", catalog.Name);
        Assert.Single(result);
    }

    [Fact]
    public async Task Recognition_UsesCompleteHeaderAndFooterNumberInsteadOfAttackDescription()
    {
        var catalog = new FakeRecognitionCatalog("Dark Charizard", "4");
        var service = new FuzzyCardSearchService(catalog, NullLogger<FuzzyCardSearchService>.Instance);
        var result = await service.SearchAsync(new("Stage 1 Dark Charizard HP 120\nFlamethrower\nDiscard all energy\n004/102", []), "pokemon", CancellationToken.None);
        Assert.Equal("Dark Charizard", catalog.Name);
        Assert.Equal("004/102", catalog.Number);
        var match = Assert.Single(result);
        Assert.Contains("holo", match.VariantCodes);
        Assert.True(match.ConfidenceScore >= 0.9);
    }

    [Fact]
    public async Task Recognition_RejectsSameNameWithDifferentCollectorNumber()
    {
        var service = new FuzzyCardSearchService(new FakeRecognitionCatalog("Pikachu", "25"), NullLogger<FuzzyCardSearchService>.Instance);
        var result = await service.SearchAsync(new("Basic Pikachu HP 40\n58/102", []), "pokemon", CancellationToken.None);
        Assert.Empty(result);
    }

    private sealed class FakeRecognitionCatalog(string name, string number) : ICardRecognitionCatalog
    {
        public string? Name { get; private set; }
        public string? Number { get; private set; }
        public Task<IReadOnlyList<RecognitionCatalogCard>> FindCandidatesAsync(string extractedName, string? extractedNumber, string gameCode, CancellationToken cancellationToken)
        {
            Name = extractedName; Number = extractedNumber;
            IReadOnlyList<RecognitionCatalogCard> cards = [new(new CatalogSearchResult(Guid.NewGuid(), "pokemon", Guid.NewGuid(), "Base Set", name, number, "en", "rare", null), ["holo"])];
            return Task.FromResult(cards);
        }
    }
}
