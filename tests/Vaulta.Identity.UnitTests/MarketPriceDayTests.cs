using Vaulta.Catalog.Application;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class MarketPriceDayTests
{
    [Theory]
    [InlineData("2026-10-01T13:00:00Z", "2026-10-01", "2026-10-02T08:00:00Z")]
    [InlineData("2026-10-02T07:59:59Z", "2026-10-01", "2026-10-02T08:00:00Z")]
    [InlineData("2026-10-02T08:00:00Z", "2026-10-02", "2026-10-03T08:00:00Z")]
    [InlineData("2026-10-02T10:00:00Z", "2026-10-02", "2026-10-03T08:00:00Z")]
    [InlineData("2026-10-05T14:00:00+02:00", "2026-10-05", "2026-10-06T08:00:00Z")]
    [InlineData("2027-01-01T02:00:00-03:00", "2026-12-31", "2027-01-01T08:00:00Z")]
    public void NextRefreshUsesFiveAmBrasiliaRatherThanElapsed24Hours(string instant, string day, string expires)
    {
        var window = MarketPriceDay.At(DateTimeOffset.Parse(instant));
        Assert.Equal(DateOnly.Parse(day), window.Date);
        Assert.Equal(DateTimeOffset.Parse(expires), window.RefreshAfter);
    }
}
