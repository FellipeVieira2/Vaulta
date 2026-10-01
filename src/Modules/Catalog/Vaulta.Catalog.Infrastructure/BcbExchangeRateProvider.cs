using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Vaulta.Catalog.Application;
using Vaulta.SharedKernel;

namespace Vaulta.Catalog.Infrastructure;

public sealed class BcbExchangeRateProvider(HttpClient httpClient, IMemoryCache cache, IClock clock, ILogger<BcbExchangeRateProvider> logger) : IBrlExchangeRateProvider
{
    public async Task<BrlExchangeRate?> GetAsync(string currency, CancellationToken cancellationToken)
    {
        if (currency is not ("USD" or "EUR")) return null;
        var key = $"scanner-fx:{currency}";
        if (cache.TryGetValue(key, out BrlExchangeRate? cached)) return cached;
        var today = clock.UtcNow.ToOffset(TimeSpan.FromHours(-3)).Date;
        var start = today.AddDays(-7).ToString("MM-dd-yyyy", CultureInfo.InvariantCulture);
        var end = today.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture);
        var path = $"CotacaoMoedaPeriodo(moeda=@moeda,dataInicial=@dataInicial,dataFinalCotacao=@dataFinalCotacao)?@moeda='{currency}'&@dataInicial='{start}'&@dataFinalCotacao='{end}'&$format=json";
        try
        {
            using var response = await httpClient.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var data = await response.Content.ReadFromJsonAsync<BcbResponse>(cancellationToken);
            var quote = data?.Value?.Where(x => x.TipoBoletim == "Fechamento" && x.CotacaoVenda > 0)
                .OrderByDescending(x => x.DataHoraCotacao, StringComparer.Ordinal).FirstOrDefault();
            if (quote is null || !DateTime.TryParse(quote.DataHoraCotacao, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)) return null;
            var result = new BrlExchangeRate(currency, quote.CotacaoVenda,
                new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified), TimeSpan.FromHours(-3)));
            cache.Set(key, result, TimeSpan.FromHours(1));
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Exchange rate lookup unavailable for {Currency}: {ErrorType}", currency, ex.GetType().Name);
            return null;
        }
    }

    private sealed record BcbResponse(BcbQuote[] Value);
    private sealed record BcbQuote(decimal CotacaoVenda, string DataHoraCotacao, string TipoBoletim);
}
