namespace Vaulta.Catalog.Application;

// A market day starts at 05:00 in Brasília, including consultations before dawn.
public sealed record MarketPriceDay(DateOnly Date, DateTimeOffset RefreshAfter)
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static MarketPriceDay At(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Brasilia);
        var date = local.Hour < 5 ? local.Date.AddDays(-1) : local.Date;
        var next = DateTime.SpecifyKind(date.AddDays(1).AddHours(5), DateTimeKind.Unspecified);
        return new(DateOnly.FromDateTime(date), new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(next, Brasilia)));
    }
}
