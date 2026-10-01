using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaulta.Identity.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.Infrastructure;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Ignore(x => x.DomainEvents); b.Ignore(x => x.CanLogin);
        b.Property(x => x.Email).HasMaxLength(254); b.Property(x => x.NormalizedEmail).HasMaxLength(254);
        b.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("ux_users_email");
        b.Property(x => x.PasswordHash).HasMaxLength(512);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne(x => x.Profile).WithOne().HasForeignKey<UserProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Preferences).WithOne().HasForeignKey<UserPreferences>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.TcgInterests).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.TcgInterests).HasField("_tcgInterests").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
internal sealed class ProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> b)
    {
        b.ToTable("user_profiles"); b.HasKey(x => x.UserId); b.Property(x => x.UserId).ValueGeneratedNever();
        b.Property(x => x.Username).HasMaxLength(30); b.Property(x => x.NormalizedUsername).HasMaxLength(30);
        b.HasIndex(x => x.NormalizedUsername).IsUnique().HasDatabaseName("ux_profiles_username");
        b.Property(x => x.DisplayName).HasMaxLength(100); b.Property(x => x.Bio).HasMaxLength(500);
        b.Property(x => x.AvatarUrl).HasMaxLength(2048); b.Property(x => x.CountryCode).HasMaxLength(2);
        b.Property(x => x.State).HasMaxLength(100); b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.ShippingStreet).HasMaxLength(200); b.Property(x => x.ShippingCity).HasMaxLength(200);
        b.Property(x => x.ShippingNumber).HasMaxLength(20); b.Property(x => x.ShippingComplement).HasMaxLength(100);
        b.Property(x => x.ShippingNeighborhood).HasMaxLength(100); b.Property(x => x.ShippingRecipient).HasMaxLength(100);
        b.Property(x => x.ShippingState).HasMaxLength(100); b.Property(x => x.ShippingZipCode).HasMaxLength(9);
    }
}
internal sealed class PreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> b)
    {
        b.ToTable("user_preferences"); b.HasKey(x => x.UserId); b.Property(x => x.UserId).ValueGeneratedNever();
        b.Property(x => x.PreferredCurrency).HasMaxLength(3); b.Property(x => x.Language).HasMaxLength(20); b.Property(x => x.TimeZone).HasMaxLength(100);
    }
}
internal sealed class InterestsConfiguration : IEntityTypeConfiguration<UserTcgInterest>
{
    public void Configure(EntityTypeBuilder<UserTcgInterest> b)
    {
        b.ToTable("user_tcg_interests"); b.HasKey(x => new { x.UserId, x.TcgCode }); b.Property(x => x.TcgCode).HasMaxLength(32);
    }
}
internal sealed class RefreshConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TokenHash).HasMaxLength(64); b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.RevokedAt }); b.HasIndex(x => x.ExpiresAt);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.ReplacedByTokenId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class OutboxConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Type).HasMaxLength(150); b.Property(x => x.Payload).HasColumnType("jsonb"); b.Property(x => x.Error).HasColumnType("text");
        b.HasIndex(x => x.OccurredAt).HasFilter("processed_at IS NULL");
    }
}
