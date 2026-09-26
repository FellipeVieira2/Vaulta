using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Vaulta.Identity.Application;
using Vaulta.Identity.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> Profiles => Set<UserProfile>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties()) property.SetColumnName(Snake(property.Name));
    }
    private static string Snake(string value) => string.Concat(value.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var aggregates = ChangeTracker.Entries<User>().Select(x => x.Entity).Where(x => x.DomainEvents.Count > 0).ToArray();
        foreach (var e in aggregates.SelectMany(x => x.DomainEvents))
        {
            if (OutboxMessages.Local.Any(x => x.Id == e.Id)) continue;
            OutboxMessages.Add(new OutboxMessage
            {
                Id = e.Id, Type = EventTypes.Name(e.GetType()), Payload = JsonSerializer.Serialize(e, e.GetType()), OccurredAt = e.OccurredAt
            });
        }
        try
        {
            var count = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            foreach (var aggregate in aggregates) aggregate.ClearDomainEvents();
            return count;
        }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Concurrent change detected. Reload and retry."); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new ConflictException(pg.ConstraintName switch
            {
                "ux_users_email" => "Email already registered.",
                "ux_profiles_username" => "Username already registered.",
                _ => "A record with this key already exists."
            });
        }
    }
}
internal static class EventTypes
{
    public static string Name(Type type) => type.Name switch
    {
        nameof(UserRegisteredDomainEvent) => "identity.user-registered.v1",
        nameof(UserProfileUpdatedDomainEvent) => "identity.user-profile-updated.v1",
        nameof(UserPasswordChangedDomainEvent) => "identity.user-password-changed.v1",
        _ => throw new InvalidOperationException("Unmapped domain event type.")
    };
}
