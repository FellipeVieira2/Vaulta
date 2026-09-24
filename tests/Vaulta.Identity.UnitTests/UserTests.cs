using System.Text.Json;
using Vaulta.Identity.Application;
using Vaulta.Identity.Contracts;
using Vaulta.Identity.Domain;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static User Create() => User.Register(" User@Example.com ", "hashed-password", "Fellipe", "Fellipe", Now);
    [Fact] public void RegistrationCreatesAggregateAndEvent()
    {
        var user = Create();
        Assert.NotEqual(Guid.Empty, user.Id); Assert.Equal("USER@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("FELLIPE", user.Profile.NormalizedUsername); Assert.Equal(user.Id, user.Profile.UserId);
        Assert.Equal(UserStatus.PendingVerification, user.Status); Assert.Null(user.EmailVerifiedAt); Assert.True(user.CanLogin);
        Assert.Equal("BRL", user.Preferences.PreferredCurrency); Assert.Equal("pt-BR", user.Preferences.Language);
        Assert.Equal("America/Sao_Paulo", user.Preferences.TimeZone); Assert.Equal(Now, user.CreatedAt);
        Assert.IsType<UserRegisteredDomainEvent>(Assert.Single(user.DomainEvents));
    }
    [Theory]
    [InlineData("ab")][InlineData("foo-bar")][InlineData("ábc")][InlineData("has space")][InlineData("abcdefghijklmnopqrstuvwxyz12345")]
    public void InvalidUsernamesAreRejected(string name) => Assert.Throws<DomainException>(() => IdentityRules.Username(name));
    [Theory][InlineData("a@EXAMPLE.com", "A@EXAMPLE.COM")][InlineData(" a@example.com ", "A@EXAMPLE.COM")]
    public void EmailNormalization(string input, string expected) => Assert.Equal(expected, IdentityRules.NormalizeEmail(input));
    [Fact] public void UpdateChangesVersionAndRaisesEvent()
    {
        var user = Create(); user.ClearDomainEvents(); var old = user.Version;
        user.UpdateProfile("New name", "Bio", "https://example.com/a.png", "br", "SP", "Araras", Now.AddMinutes(1));
        Assert.Equal("BR", user.Profile.CountryCode); Assert.Equal("New name", user.Profile.DisplayName);
        Assert.NotEqual(old, user.Version); Assert.Equal(Now.AddMinutes(1), user.UpdatedAt);
        Assert.IsType<UserProfileUpdatedDomainEvent>(Assert.Single(user.DomainEvents));
    }
    [Fact] public void InvalidCountryAndOversizeBioAreRejected()
    {
        Assert.Throws<DomainException>(() => Create().UpdateProfile("Name", null, null, "ZZ", null, null, Now));
        Assert.Throws<DomainException>(() => Create().UpdateProfile("Name", new string('a', 501), null, "BR", null, null, Now));
    }
    [Fact] public void PasswordChangeInvalidatesStampAndEmitsEvent()
    {
        var user = Create(); user.ClearDomainEvents(); var stamp = user.SecurityStamp;
        user.ChangePassword("new-hash", Now);
        Assert.Equal("new-hash", user.PasswordHash); Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.IsType<UserPasswordChangedDomainEvent>(Assert.Single(user.DomainEvents));
    }
    [Fact] public void PreferencesValidateAndDeduplicateInterests()
    {
        var user = Create(); user.UpdatePreferences("usd", "en-US", "America/New_York", ["pokemon", "POKEMON"], Now);
        Assert.Single(user.TcgInterests); Assert.Equal("USD", user.Preferences.PreferredCurrency);
        Assert.Throws<DomainException>(() => user.UpdatePreferences("BRL", "pt-BR", "America/Sao_Paulo", ["UNKNOWN"], Now));
    }
    [Fact] public void PatchDistinguishesOmittedAndNull()
    {
        var patch = JsonSerializer.Deserialize<ProfilePatch>("{\"bio\":null}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(patch.HasBio); Assert.Null(patch.Bio); Assert.False(patch.HasDisplayName);
    }
    [Theory][InlineData("short")][InlineData("onlylowercasecharacters")][InlineData("UPPERCASE123456!")]
    public void WeakPasswordRejected(string password) => Assert.False(new PasswordValidator().Validate(password).IsValid);
    [Fact] public void MoneyUsesDecimalAndNormalizedCurrency() => Assert.Equal(new Money(10.25m, "BRL"), new Money(10.25m, "brl"));
}
