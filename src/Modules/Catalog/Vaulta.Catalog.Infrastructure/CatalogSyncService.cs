using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Catalog.Infrastructure;

public sealed class CatalogSyncService(CatalogDbContext db, IEnumerable<ICatalogProvider> providers, IClock clock, ILogger<CatalogSyncService> logger) : ICatalogSync
{
    private const string GameCode = "pokemon";
    private const string EntitySet = "set";
    private const string EntityCard = "card";
    private const string EntityPrinting = "printing";

    public async Task<Guid> Synchronize(string provider, string scope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 200) throw new ArgumentException("Invalid sync scope.", nameof(scope));
        scope = scope.Trim();
        if (scope.Equals("all", StringComparison.OrdinalIgnoreCase)) scope = "all";
        var source = providers.SingleOrDefault(x => x.Code.Equals(provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unknown catalog provider.", nameof(provider));
        var game = await db.Games.SingleOrDefaultAsync(x => x.Code == GameCode, cancellationToken)
            ?? throw new InvalidOperationException("The Pokemon game must be seeded before synchronization.");
        var run = new CatalogSyncRun { Id = Guid.NewGuid(), Provider = source.Code, Scope = scope, StartedAt = clock.UtcNow, Status = "running" };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["SyncRunId"] = run.Id, ["Provider"] = source.Code, ["Scope"] = scope
        });
        logger.LogInformation("Catalog sync started");
        // Provider-wide: `all` and a single set must not mutate overlapping identities concurrently.
        var advisoryKey = AdvisoryKey(source.Code, "catalog");
        var acquired = false;
        var opened = false;
        string? currentSetId = null;
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            opened = true;
            var connection = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@key)";
            command.Parameters.AddWithValue("key", advisoryKey);
            acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            if (!acquired)
            {
                run.Status = "skipped";
                return run.Id;
            }
            var sets = await source.GetSets(cancellationToken);
            run.RecordsRead += sets.Count;
            var selected = sets.Where(x => scope == "all" || x.ExternalId.Equals(scope, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (scope != "all" && selected.Length == 0)
                throw new CatalogProviderException("Requested set was not found in the provider catalog.", false, "scope_not_found");
            foreach (var providerSet in selected)
            {
                currentSetId = providerSet.ExternalId;
                using var setLogScope = logger.BeginScope(new Dictionary<string, object> { ["SetId"] = providerSet.ExternalId });
                var details = await source.GetSetDetails(providerSet.ExternalId, cancellationToken);
                if (details.Set.ExternalId != providerSet.ExternalId)
                    throw new CatalogProviderException("Set details do not match the requested identity.", false, "invalid_contract");
                run.RecordsRead += details.Printings.Count;
                var counters = (run.RecordsCreated, run.RecordsUpdated, run.RecordsUnresolved);
                try
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                    var set = await UpsertSet(source.Code, game, details.Set, run, cancellationToken);
                    var seenPrintingIds = new List<Guid>(details.Printings.Count);
                    foreach (var printing in details.Printings)
                        seenPrintingIds.Add(await UpsertPrinting(source.Code, game, set, printing, run, cancellationToken));
                    await DeactivateMissingPrintings(set, seenPrintingIds, source.Language, run, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    logger.LogInformation("Catalog set persisted with {PrintingCount} printings", details.Printings.Count);
                }
                catch
                {
                    (run.RecordsCreated, run.RecordsUpdated, run.RecordsUnresolved) = counters;
                    throw;
                }
                // Bound EF tracking to one set when ingesting the complete catalog.
                db.ChangeTracker.Clear();
                db.SyncRuns.Attach(run);
            }
            run.Status = "completed";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = "cancelled";
            run.ErrorCategory = "cancelled";
            throw;
        }
        catch (Exception e)
        {
            run.Status = "failed";
            run.ErrorCategory = e is CatalogProviderException failure
                ? $"{(failure.IsTransient ? "transient" : "permanent")}:{failure.ErrorCategory}"
                : e.GetType().Name;
            logger.LogWarning("Catalog sync failed for set {SetId} with {ErrorType}", currentSetId, run.ErrorCategory);
            throw;
        }
        finally
        {
            // Never flush half an unsuccessful set while recording its failure/cancellation.
            db.ChangeTracker.Clear();
            run.CompletedAt = clock.UtcNow;
            db.SyncRuns.Update(run);
            try { await db.SaveChangesAsync(CancellationToken.None); }
            finally
            {
                try
                {
                    if (acquired)
                    {
                        await using var unlock = db.Database.GetDbConnection().CreateCommand();
                        unlock.CommandText = "SELECT pg_advisory_unlock(@key)";
                        var parameter = unlock.CreateParameter(); parameter.ParameterName = "key"; parameter.Value = advisoryKey;
                        unlock.Parameters.Add(parameter);
                        await unlock.ExecuteNonQueryAsync(CancellationToken.None);
                    }
                }
                finally { if (opened) await db.Database.CloseConnectionAsync(); }
            }
            logger.LogInformation("Catalog sync finished with {Status} and {ErrorType}", run.Status, run.ErrorCategory);
        }
        return run.Id;
    }

    private async Task<Set> UpsertSet(string providerCode, Game game, ProviderSet input, CatalogSyncRun run, CancellationToken ct)
    {
        var external = await db.ExternalIds.SingleOrDefaultAsync(x => x.Provider == providerCode && x.EntityType == EntitySet && x.ExternalId == input.ExternalId, ct);
        var normalizedName = CatalogNormalizer.NormalizeName(input.Name);
        Set set;
        var created = external is null;
        if (external is not null)
            set = await db.Sets.SingleAsync(x => x.Id == external.EntityId, ct);
        else
        {
            set = new Set { Id = Guid.NewGuid(), GameId = game.Id, Code = input.Code, Name = input.Name, NormalizedName = normalizedName,
                ReleaseDate = input.ReleaseDate?.ToString("yyyy-MM-dd") };
            db.Sets.Add(set);
            external = NewExternal(providerCode, EntitySet, set.Id, input.ExternalId, input);
            db.ExternalIds.Add(external);
        }
        var changed = set.Name != input.Name || set.NormalizedName != normalizedName || set.ReleaseDate != input.ReleaseDate?.ToString("yyyy-MM-dd");
        if (!created && changed)
        {
            set.Name = input.Name; set.NormalizedName = normalizedName; set.ReleaseDate = input.ReleaseDate?.ToString("yyyy-MM-dd");
            run.RecordsUpdated++;
        }
        else if (created) run.RecordsCreated++;
        UpdateExternal(external, input);
        return set;
    }

    private async Task<Guid> UpsertPrinting(string providerCode, Game game, Set set, ProviderPrinting input, CatalogSyncRun run, CancellationToken ct)
    {
        // TCGdex reuses the same card ID in every language. Preserve legacy
        // English identities, and namespace other languages rather than
        // overwriting a printing already owned by collectors.
        var sourceId = providerCode == "tcgdex" && CatalogNormalizer.NormalizeLanguage(input.Language) != "en"
            ? $"{CatalogNormalizer.NormalizeLanguage(input.Language)}:{input.ExternalId}" : input.ExternalId;
        var cardExternal = await db.ExternalIds.SingleOrDefaultAsync(x => x.Provider == providerCode && x.EntityType == EntityCard && x.ExternalId == sourceId, ct);
        Card card;
        if (cardExternal is null)
        {
            card = new Card { Id = Guid.NewGuid(), GameId = game.Id, Name = input.Name, NormalizedName = CatalogNormalizer.NormalizeName(input.Name) };
            db.Cards.Add(card);
            run.RecordsUnresolved++; // No authoritative cross-printing Card identity from this source.
            cardExternal = NewExternal(providerCode, EntityCard, card.Id, sourceId, input);
            db.ExternalIds.Add(cardExternal);
            run.RecordsCreated++;
        }
        else
        {
            card = await db.Cards.SingleAsync(x => x.Id == cardExternal.EntityId, ct);
            var normalized = CatalogNormalizer.NormalizeName(input.Name);
            if (card.Name != input.Name || card.NormalizedName != normalized)
            {
                card.Name = input.Name; card.NormalizedName = normalized; run.RecordsUpdated++;
            }
            UpdateExternal(cardExternal, input);
        }

        var printingExternal = await db.ExternalIds.SingleOrDefaultAsync(x => x.Provider == providerCode && x.EntityType == EntityPrinting && x.ExternalId == sourceId, ct);
        Printing printing;
        if (printingExternal is null)
        {
            printing = new Printing { Id = Guid.NewGuid(), CardId = card.Id, SetId = set.Id, CollectorNumber = input.CollectorNumber,
                NormalizedCollectorNumber = CatalogNormalizer.NormalizeCollectorNumber(input.CollectorNumber), Language = CatalogNormalizer.NormalizeLanguage(input.Language),
                Rarity = CatalogNormalizer.NormalizeCode(input.Rarity), RawRarity = input.Rarity,
                ExternalArtworkUrl = input.ImageUrl, ArtworkProvider = input.ImageUrl is null ? null : providerCode, IsActive = true };
            db.Printings.Add(printing);
            db.ExternalIds.Add(NewExternal(providerCode, EntityPrinting, printing.Id, sourceId, input));
            run.RecordsCreated++;
        }
        else
        {
            printing = await db.Printings.Include(x => x.Variants).SingleAsync(x => x.Id == printingExternal.EntityId, ct);
            printing.CardId = card.Id; printing.SetId = set.Id;
            var changed = printing.CollectorNumber != input.CollectorNumber || printing.Language != CatalogNormalizer.NormalizeLanguage(input.Language) || printing.Rarity != CatalogNormalizer.NormalizeCode(input.Rarity) || printing.RawRarity != input.Rarity ||
                printing.ExternalArtworkUrl != input.ImageUrl || printing.ArtworkProvider != (input.ImageUrl is null ? null : providerCode) || !printing.IsActive;
            if (changed)
            {
                printing.CollectorNumber = input.CollectorNumber;
                printing.NormalizedCollectorNumber = CatalogNormalizer.NormalizeCollectorNumber(input.CollectorNumber);
                printing.Language = CatalogNormalizer.NormalizeLanguage(input.Language);
                printing.Rarity = CatalogNormalizer.NormalizeCode(input.Rarity); printing.RawRarity = input.Rarity;
                printing.ExternalArtworkUrl = input.ImageUrl; printing.ArtworkProvider = input.ImageUrl is null ? null : providerCode;
                printing.IsActive = true;
                run.RecordsUpdated++;
            }
            var ext = await db.ExternalIds.SingleAsync(x => x.Id == printingExternal.Id, ct);
            UpdateExternal(ext, input);
        }

        var variants = input.Variants.ToDictionary(x => x.Code, StringComparer.Ordinal);
        foreach (var existing in printing.Variants)
        {
            var active = variants.TryGetValue(existing.Code, out var current);
            if (existing.IsActive != active || (current is not null && (existing.Name != current.Name || existing.RawValue != current.RawValue)))
            {
                existing.IsActive = active;
                if (current is not null) { existing.Name = current.Name; existing.RawValue = current.RawValue; }
                run.RecordsUpdated++;
            }
        }
        foreach (var variant in input.Variants.Where(v => printing.Variants.All(x => x.Code != v.Code)))
        {
            printing.Variants.Add(new Variant { Id = Guid.NewGuid(), Code = variant.Code, Name = variant.Name, RawValue = variant.RawValue });
            run.RecordsCreated++;
        }
        return printing.Id;
    }

    private async Task DeactivateMissingPrintings(Set set, IReadOnlyCollection<Guid> seenPrintingIds, string? language, CatalogSyncRun run, CancellationToken ct)
    {
        // Only reached after every printing in this provider batch has been upserted successfully; a Printing
        // previously known for this Set that the provider no longer returns becomes unavailable, not deleted.
        var toDeactivate = await db.Printings.Where(x => x.SetId == set.Id && x.IsActive && (language == null || x.Language == language)
            && !seenPrintingIds.Contains(x.Id)).ToArrayAsync(ct);
        foreach (var printing in toDeactivate)
        {
            printing.IsActive = false;
            run.RecordsUpdated++;
        }
    }

    private CatalogExternalId NewExternal(string providerCode, string entityType, Guid entityId, string externalId, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new CatalogExternalId { Id = Guid.NewGuid(), Provider = providerCode, EntityType = entityType, EntityId = entityId,
            ExternalId = externalId, ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))), FirstSeenAt = clock.UtcNow, LastSeenAt = clock.UtcNow };
    }

    private void UpdateExternal(CatalogExternalId external, object payload)
    {
        external.ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
        external.LastSeenAt = clock.UtcNow;
    }

    private static long AdvisoryKey(string provider, string scope) => BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}:{scope}")), 0);
}
