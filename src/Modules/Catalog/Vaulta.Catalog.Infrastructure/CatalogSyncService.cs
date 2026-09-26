using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Domain;
using Vaulta.Catalog.Contracts;
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
        var source = providers.SingleOrDefault(x => x.Code.Equals(provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unknown catalog provider.", nameof(provider));
        var game = await db.Games.SingleOrDefaultAsync(x => x.Code == GameCode, cancellationToken)
            ?? throw new InvalidOperationException("The Pokemon game must be seeded before synchronization.");
        var run = new CatalogSyncRun { Id = Guid.NewGuid(), Provider = source.Code, Scope = scope, StartedAt = clock.UtcNow, Status = "running" };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var advisoryKey = AdvisoryKey(source.Code, scope);
            var lockConnection = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
            if (lockConnection.State != System.Data.ConnectionState.Open) await lockConnection.OpenAsync(cancellationToken);
            await using var command = lockConnection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@key)";
            command.Parameters.AddWithValue("key", advisoryKey);
            var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            if (!acquired)
            {
                run.Status = "skipped";
                run.CompletedAt = clock.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return run.Id;
            }

            try
            {
                var sets = await source.GetSets(cancellationToken);
                run.RecordsRead += sets.Count;
                foreach (var providerSet in sets.Where(x => scope == "all" || x.ExternalId.Equals(scope, StringComparison.OrdinalIgnoreCase)))
                {
                    var set = await UpsertSet(game, providerSet, run, cancellationToken);
                    var printings = await source.GetPrintings(providerSet.ExternalId, cancellationToken);
                    run.RecordsRead += printings.Count;
                    foreach (var providerPrinting in printings)
                        await UpsertPrinting(game, set, providerPrinting, run, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                }
                run.Status = "completed";
            }
            finally
            {
                await using var unlock = lockConnection.CreateCommand();
                unlock.CommandText = "SELECT pg_advisory_unlock(@key)";
                unlock.Parameters.AddWithValue("key", advisoryKey);
                await unlock.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = "cancelled";
            run.ErrorCategory = "OperationCanceledException";
            throw;
        }
        catch (Exception e)
        {
            run.Status = "failed";
            run.ErrorCategory = e.GetType().Name;
            logger.LogWarning("Catalog sync {SyncRunId} failed with {ErrorType}", run.Id, run.ErrorCategory);
            throw;
        }
        finally
        {
            run.CompletedAt = clock.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return run.Id;
    }

    private async Task<Set> UpsertSet(Game game, ProviderSet input, CatalogSyncRun run, CancellationToken ct)
    {
        var external = await db.ExternalIds.SingleOrDefaultAsync(x => x.Provider == "tcgdex" && x.EntityType == EntitySet && x.ExternalId == input.ExternalId, ct);
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
            external = NewExternal(EntitySet, set.Id, input.ExternalId, input);
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

    private async Task UpsertPrinting(Game game, Set set, ProviderPrinting input, CatalogSyncRun run, CancellationToken ct)
    {
        var cardExternal = await db.ExternalIds.SingleOrDefaultAsync(x => x.Provider == "tcgdex" && x.EntityType == EntityCard && x.ExternalId == input.ExternalId, ct);
        Card card;
        if (cardExternal is null)
        {
            card = new Card { Id = Guid.NewGuid(), GameId = game.Id, Name = input.Name, NormalizedName = CatalogNormalizer.NormalizeName(input.Name) };
            db.Cards.Add(card);
            cardExternal = NewExternal(EntityCard, card.Id, input.ExternalId, input);
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

        var printingExternal = await db.ExternalIds.SingleOrDefaultAsync(x => x.Provider == "tcgdex" && x.EntityType == EntityPrinting && x.ExternalId == input.ExternalId, ct);
        Printing printing;
        if (printingExternal is null)
        {
            printing = new Printing { Id = Guid.NewGuid(), CardId = card.Id, SetId = set.Id, CollectorNumber = input.CollectorNumber,
                NormalizedCollectorNumber = CatalogNormalizer.NormalizeCollectorNumber(input.CollectorNumber), Language = CatalogNormalizer.NormalizeLanguage(input.Language),
                Rarity = CatalogNormalizer.NormalizeCode(input.Rarity), RawRarity = input.Rarity };
            db.Printings.Add(printing);
            db.ExternalIds.Add(NewExternal(EntityPrinting, printing.Id, input.ExternalId, input));
            run.RecordsCreated++;
        }
        else
        {
            printing = await db.Printings.Include(x => x.Variants).SingleAsync(x => x.Id == printingExternal.EntityId, ct);
            printing.CardId = card.Id; printing.SetId = set.Id;
            var changed = printing.CollectorNumber != input.CollectorNumber || printing.Language != CatalogNormalizer.NormalizeLanguage(input.Language) || printing.Rarity != CatalogNormalizer.NormalizeCode(input.Rarity);
            if (changed)
            {
                printing.CollectorNumber = input.CollectorNumber;
                printing.NormalizedCollectorNumber = CatalogNormalizer.NormalizeCollectorNumber(input.CollectorNumber);
                printing.Language = CatalogNormalizer.NormalizeLanguage(input.Language);
                printing.Rarity = CatalogNormalizer.NormalizeCode(input.Rarity); printing.RawRarity = input.Rarity;
                run.RecordsUpdated++;
            }
            var ext = await db.ExternalIds.SingleAsync(x => x.Id == printingExternal.Id, ct);
            UpdateExternal(ext, input);
        }

        var variants = input.Variants.Select(CatalogNormalizer.NormalizeCode).Where(x => x is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var existing in printing.Variants.Where(x => !variants.Contains(x.Code)).ToArray()) db.Variants.Remove(existing);
        foreach (var variant in variants.Where(code => printing.Variants.All(x => x.Code != code)))
            printing.Variants.Add(new Variant { Id = Guid.NewGuid(), Code = variant, Name = variant, RawValue = input.Variants.First(x => CatalogNormalizer.NormalizeCode(x) == variant) });
    }

    private CatalogExternalId NewExternal(string entityType, Guid entityId, string externalId, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new CatalogExternalId { Id = Guid.NewGuid(), Provider = "tcgdex", EntityType = entityType, EntityId = entityId,
            ExternalId = externalId, ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))), FirstSeenAt = clock.UtcNow, LastSeenAt = clock.UtcNow };
    }

    private void UpdateExternal(CatalogExternalId external, object payload)
    {
        external.ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
        external.LastSeenAt = clock.UtcNow;
    }

    private static long AdvisoryKey(string provider, string scope) => BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}:{scope}")), 0);
}
