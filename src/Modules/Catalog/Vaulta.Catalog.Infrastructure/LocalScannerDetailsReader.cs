using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.SharedKernel;
namespace Vaulta.Catalog.Infrastructure;
// Request path: PostgreSQL only. Expiration schedules refresh; it never triggers provider HTTP here.
public sealed class LocalScannerDetailsReader(CatalogDbContext db,ICatalogSearch catalog,IClock clock) : IScannerCardDetailsReader
{
    public async Task<ScannerCardDetailsDto?> GetAsync(Guid printingId,CancellationToken ct)
    {
        var printing=await catalog.GetPrinting(printingId,ct); if(printing is null) return null;
        var metadata=await db.Printings.AsNoTracking().Where(x=>x.Id==printingId).Select(x=>x.MetadataJson).SingleAsync(ct);
        using var information=JsonDocument.Parse(metadata ?? "{}");
        var fields=TcgDexScannerDetailsReader.ReadInformation(information.RootElement);
        var snapshot=await db.DailyMarketSnapshots.AsNoTracking().Where(x=>x.PrintingId==printingId).OrderByDescending(x=>x.MarketDay).FirstOrDefaultAsync(ct);
        if(snapshot is null) return new(printing,fields,[],"Preço indisponível no catálogo local.");
        var stored=JsonSerializer.Deserialize<ScannerCardDetailsDto>(snapshot.Payload) ?? throw new JsonException("Invalid local market snapshot.");
        var active=printing.Variants.Select(x=>x.Id).ToHashSet();
        var quotes=stored.MarketQuotes.Where(x=>active.Contains(x.VariantId)).ToArray();
        return stored with { Printing=printing,Information=fields,MarketQuotes=quotes,FetchedAt=snapshot.FetchedAt,NextRefreshAt=snapshot.RefreshAfter,
            Notice=quotes.Length==0 ? "Preço indisponível no catálogo local." : snapshot.RefreshAfter<=clock.UtcNow ? "Referência armazenada; atualização de preço pendente." : stored.Notice };
    }
}
