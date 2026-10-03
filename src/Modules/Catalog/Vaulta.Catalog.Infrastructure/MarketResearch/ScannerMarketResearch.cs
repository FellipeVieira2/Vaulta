using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
using Vaulta.SharedKernel;

namespace Vaulta.Catalog.Infrastructure.MarketResearch;

public sealed class ScannerMarketResearch(CatalogDbContext db, IScannerWebMarketResearchProvider provider, IClock clock,
    IOptions<ScannerMarketResearchOptions> options) : IScannerMarketResearch
{
    public async Task<ScannerMarketResearchResultDto?> ResearchAsync(CardVisualIdentificationDto identification, byte[]? image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.Enabled) return null;
        var clean = MarketResearchValidation.WithoutSerial(identification);
        var key = Key(clean);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Includes advisory-lock waiting and both external providers. The HTTP scanner additionally bounds the whole identify operation.
        deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds + 15));
        var ct = deadline.Token;
        try
        {
            var cached = await Read(key, ct);
            if (cached is not null) return Restore(cached, identification, cacheHit: true);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var day = MarketPriceDay.At(clock.UtcNow);
            var lockKey = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"Vaulta:scanner:research:{key}:{day.Date:yyyy-MM-dd}")));
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
            cached = await Read(key, ct);
            if (cached is not null) return Restore(cached, identification, cacheHit: true);
            ScannerMarketResearchResultDto? result;
            try { result = await provider.ResearchAsync(clean, image, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { result = null; }
            var outcome = result?.Estimate is not null ? "quoted" : result is not null ? "identity_only" : "transient_miss";
            result ??= new(clean, null, "research_unavailable");
            var corrected = MarketResearchValidation.WithoutSerial(result.Identification);
            var estimate = result.Estimate;
            // Never create a broad positive entry for incomplete physical identity.
            if (!Complete(corrected)) { estimate = null; outcome = "unresolved"; }
            var refresh = estimate is not null ? MarketPriceDay.At(clock.UtcNow).RefreshAfter
                : clock.UtcNow.AddMinutes(outcome == "transient_miss" ? 2 : 10);
            result = result with { Identification = corrected, Estimate = estimate is null ? null : estimate with { Identification = corrected, NextRefreshAt = refresh } };
            var correctedKey = Key(corrected);
            var payload = JsonSerializer.Serialize(result);
            var fetchedAt = clock.UtcNow;
            // Corrections are written exclusively under the corrected key. A misread number never aliases another printing.
            // Atomic upsert permits different uncertain captures to converge without unique-key races.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO catalog.scanner_research_snapshots (cache_key, market_day, fetched_at, refresh_after, outcome, payload)
                VALUES ({correctedKey}, {day.Date}, {fetchedAt}, {refresh}, {outcome}, CAST({payload} AS jsonb))
                ON CONFLICT (cache_key) DO UPDATE SET market_day = EXCLUDED.market_day, fetched_at = EXCLUDED.fetched_at,
                    refresh_after = EXCLUDED.refresh_after, outcome = EXCLUDED.outcome, payload = EXCLUDED.payload
                WHERE scanner_research_snapshots.refresh_after <= {fetchedAt}
                """, ct);
            await transaction.CommitAsync(ct);
            return Restore(result, identification);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(identification, null, "research_timeout"); }
    }

    private async Task<ScannerMarketResearchResultDto?> Read(string key, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var snapshot = await db.ScannerResearchSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.CacheKey == key && x.RefreshAfter > now, ct);
        return snapshot is null ? null : JsonSerializer.Deserialize<ScannerMarketResearchResultDto>(snapshot.Payload);
    }

    private static ScannerMarketResearchResultDto Restore(ScannerMarketResearchResultDto result, CardVisualIdentificationDto capture, bool cacheHit = false)
    {
        // Cached market/catalog certainty is not evidence that this new photograph is the same physical card.
        var card = result.Identification with { Certification = capture.Certification,
            Confidence = cacheHit ? Math.Min(result.Identification.Confidence, capture.Confidence) : result.Identification.Confidence };
        return result with { Identification = card, Estimate = result.Estimate is null ? null : result.Estimate with { Identification = card,
            Confidence = cacheHit ? Math.Min(result.Estimate.Confidence, capture.Confidence) : result.Estimate.Confidence } };
    }

    private static bool Complete(CardVisualIdentificationDto card) => !string.IsNullOrWhiteSpace(card.GameCode)
        && !string.IsNullOrWhiteSpace(card.Name) && !string.IsNullOrWhiteSpace(card.CollectorNumber) && !string.IsNullOrWhiteSpace(card.Language)
        && !string.IsNullOrWhiteSpace(card.SetName) && !string.IsNullOrWhiteSpace(card.Finish) && (card.Certification?.IsGraded != true
            || !string.IsNullOrWhiteSpace(card.Certification.Company) && !string.IsNullOrWhiteSpace(card.Certification.Grade));

    private static string Key(CardVisualIdentificationDto card)
    {
        static string? Normalize(string? value) => value is null ? null : Regex.Replace(value.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant(), @"\s+", " ");
        var material = JsonSerializer.Serialize(new object?[] { "v1", Normalize(card.GameCode), Normalize(card.Name), Normalize(card.CollectorNumber),
            Normalize(card.Language), Normalize(card.SetName), Normalize(card.Finish), card.Certification?.IsGraded == true,
            Normalize(card.Certification?.Company), Normalize(card.Certification?.Grade), Normalize(card.SurfaceTreatment), Normalize(card.Condition),
            card.Hp, Normalize(card.Attributes?.Rarity), card.Attributes?.Year, Normalize(card.Attributes?.CardType), Normalize(card.Attributes?.Stage) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
