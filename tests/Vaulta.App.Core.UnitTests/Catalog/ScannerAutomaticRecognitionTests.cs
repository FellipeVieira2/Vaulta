using Vaulta.App.Core.Catalog;
using Vaulta.Catalog.Contracts;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Catalog;

public sealed class ScannerAutomaticRecognitionTests
{
    [Fact]
    public void StrongUniqueNumberMatchCanBeAccepted()
    {
        var candidate = Card(.98);
        Assert.Equal(candidate, ScannerAutomaticRecognition.Select(new([candidate])));
    }

    [Theory]
    [InlineData(.92, true)]
    [InlineData(.99, false)]
    [InlineData(double.NaN, true)]
    [InlineData(double.PositiveInfinity, true)]
    public void WeakOrInvalidEvidenceCannotBeAccepted(double score, bool numberMatch) =>
        Assert.Null(ScannerAutomaticRecognition.Select(new([Card(score, numberMatch)])));

    [Fact]
    public void CloseCandidatesRequireChoice() =>
        Assert.Null(ScannerAutomaticRecognition.Select(new([Card(.98), Card(.89)])));

    [Fact]
    public void ASecondReadThatChangesIdentityCannotAutoAccept()
    {
        var first = Card(.8); var second = Card(.99);
        Assert.Null(ScannerAutomaticRecognition.Select(new([second]), first.PrintingId));
        Assert.Equal(first.PrintingId, ScannerAutomaticRecognition.Select(new([first with { ConfidenceScore = .99 }]), first.PrintingId)?.PrintingId);
    }

    [Fact]
    public void NoCandidateOrEmptyIdentityCannotAutoAccept()
    {
        Assert.Null(ScannerAutomaticRecognition.Select(new([])));
        Assert.Null(ScannerAutomaticRecognition.Select(new([Card(.99) with { PrintingId = Guid.Empty }])));
    }

    private static CardScanCandidateDto Card(double score, bool number = true) =>
        new(Guid.NewGuid(), "Carta", "Set", "1", null, null, null, null, [], score, HasCollectorNumberMatch: number);
}
