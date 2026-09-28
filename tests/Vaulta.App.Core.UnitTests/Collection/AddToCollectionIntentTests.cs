using Vaulta.App.Core.Collection;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Collection;

public sealed class AddToCollectionIntentTests
{
    [Fact]
    public void SameIntentInstance_ReusesTheSameIdempotencyKeyAcrossRetries()
    {
        var intent = new AddToCollectionIntent();

        var firstAttemptKey = intent.IdempotencyKey;
        var retryKey = intent.IdempotencyKey; // simulates re-reading the key for a retry of the same request

        Assert.Equal(firstAttemptKey, retryKey);
    }

    [Fact]
    public void NewVoluntaryAction_CreatesANewIdempotencyKey()
    {
        var firstIntent = new AddToCollectionIntent();
        var secondIntent = new AddToCollectionIntent();

        Assert.NotEqual(firstIntent.IdempotencyKey, secondIntent.IdempotencyKey);
    }
}
