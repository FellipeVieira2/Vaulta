using Microsoft.EntityFrameworkCore;
using Vaulta.Reviews.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Reviews.Infrastructure;

public sealed class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options) : DbContext(options)
{
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("reviews");

        modelBuilder.Entity<Review>(b =>
        {
            b.ToTable("reviews");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.OrderId).IsRequired();
            b.Property(x => x.ReviewerId).IsRequired();
            b.Property(x => x.ReviewedUserId).IsRequired();
            b.Property(x => x.ReviewedRole).HasMaxLength(10).HasDefaultValue("UNKNOWN").IsRequired();
            b.Property(x => x.Rating).IsRequired();
            b.Property(x => x.Comment).HasMaxLength(2000);
            b.Ignore(x => x.DomainEvents);

            b.HasIndex(x => new { x.OrderId, x.ReviewerId }).IsUnique().HasDatabaseName("ux_reviews_review_order_reviewer");
            b.HasIndex(x => x.ReviewedUserId).HasDatabaseName("ix_reviews_review_reviewed_user");
            b.HasIndex(x => new { x.ReviewedUserId, x.ReviewedRole }).HasDatabaseName("ix_reviews_review_user_role");
            b.HasIndex(x => x.OrderId).HasDatabaseName("ix_reviews_review_order");
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages", "identity", table => table.ExcludeFromMigrations());
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Type).HasMaxLength(150).IsRequired();
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Error).HasColumnType("text");
        });

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(Snake(property.Name));
    }

    private static string Snake(string value) => string.Concat(value.Select((c, i) =>
        char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var reviews = ChangeTracker.Entries<Review>()
            .Select(x => x.Entity).Where(x => x.DomainEvents.Count > 0).ToArray();
        var events = reviews.SelectMany(x => x.DomainEvents).ToArray();

        foreach (var domainEvent in events)
        {
            OutboxMessages.Add(new OutboxMessage
            {
                Id = domainEvent.Id,
                Type = EventType(domainEvent),
                Payload = System.Text.Json.JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                OccurredAt = domainEvent.OccurredAt
            });
        }

        try
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            foreach (var aggregate in reviews) aggregate.ClearDomainEvents();
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Review record changed. Reload and retry.");
        }
    }

    private static string EventType(IDomainEvent domainEvent) => domainEvent switch
    {
        ReviewCreatedDomainEvent => "reviews.review-created.v1",
        _ => throw new InvalidOperationException("Unmapped reviews domain event.")
    };
}
